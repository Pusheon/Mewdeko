using System.Collections.Concurrent;
using System.Threading;
using Discord;
using Discord.WebSocket;
using Humanizer;
using Mewdeko._Extensions;
using Mewdeko.Common.Collections;
using Mewdeko.Database;
using Mewdeko.Database.Extensions;
using Mewdeko.Database.Models;
using Mewdeko.Modules.Administration.Common;
using Mewdeko.Modules.Moderation.Services;
using Mewdeko.Services.strings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Mewdeko.Modules.Administration.Services;

public class LogCommandService : INService
{
    public enum LogType
    {
        Other,
        MessageUpdated,
        MessageDeleted,
        UserJoined,
        UserLeft,
        UserBanned,
        UserUnbanned,
        UserUpdated,
        ChannelCreated,
        ChannelDestroyed,
        ChannelUpdated,
        VoicePresence,
        VoicePresenceTts,
        UserMuted
    }

    private readonly DiscordSocketClient _client;
    private readonly DbService _db;
    private readonly ConcurrentHashSet<ulong> _ignoreMessageIds = new();
    private readonly IMemoryCache _memoryCache;
    private readonly IBotStrings _strings;
    private readonly Mewdeko _bot;

    private readonly GuildTimezoneService _tz;

    public readonly Timer ClearTimer;

    public LogCommandService(DiscordSocketClient client, IBotStrings strings,
        DbService db, MuteService mute, ProtectionService prot, GuildTimezoneService tz,
        IMemoryCache memoryCache, Mewdeko bot)
    {
        _bot = bot;
        _client = client;
        _memoryCache = memoryCache;
        _strings = strings;
        _db = db;
        _tz = tz;

        using (var uow = db.GetDbContext())
        {
            var guildIds = client.Guilds.Select(x => x.Id).ToList();
            var configs = uow.GuildConfigs
                             .AsQueryable()
                             .Include(gc => gc.LogSetting)
                             .ThenInclude(ls => ls.IgnoredChannels)
                             .Where(x => guildIds.Contains(x.GuildId))
                             .ToList();

            GuildLogSettings = configs
                .ToDictionary(g => g.GuildId, g => g.LogSetting)
                .ToConcurrent();
        }

        //_client.MessageReceived += Client_MessageReceived;
        _client.MessageUpdated += Client_MessageUpdated;
        _client.MessageDeleted += Client_MessageDeleted;
        _client.MessagesBulkDeleted += Client_BulkDelete;
        _client.UserBanned += Client_UserBanned;
        _client.UserUnbanned += Client_UserUnbanned;
        _client.UserJoined += Client_UserJoined;
        _client.UserLeft += Client_UserLeft;
        _client.UserVoiceStateUpdated += Client_UserVoiceStateUpdated;
        _client.UserVoiceStateUpdated += Client_UserVoiceStateUpdated_TTS;
        _client.GuildMemberUpdated += Client_GuildUserUpdated;
#if !GLOBAL_Mewdeko
        _client.UserUpdated += Client_UserUpdated;
#endif
        _client.ChannelCreated += Client_ChannelCreated;
        _client.ChannelDestroyed += Client_ChannelDestroyed;
        _client.ChannelUpdated += Client_ChannelUpdated;
        _client.RoleDeleted += Client_RoleDeleted;

        mute.UserMuted += MuteCommands_UserMuted;
        mute.UserUnmuted += MuteCommands_UserUnmuted;
        //_client.ThreadCreated += ThreadCreated;
        prot.OnAntiProtectionTriggered += TriggeredAntiProtection;

        ClearTimer = new Timer(_ => _ignoreMessageIds.Clear(), null, TimeSpan.FromHours(1),
            TimeSpan.FromHours(1));
    }
    


    public ConcurrentDictionary<ulong, LogSetting> GuildLogSettings { get; }

    private ConcurrentDictionary<ITextChannel, List<string>> PresenceUpdates { get; } =
        new();

    public void AddDeleteIgnore(ulong messageId) => _ignoreMessageIds.Add(messageId);

    public bool LogIgnore(ulong gid, ulong cid)
    {
        int removed;
        using (var uow = _db.GetDbContext())
        {
            var config = uow.LogSettingsFor(gid);
            var logSetting = GuildLogSettings.GetOrAdd(gid, _ => config.LogSetting);
            removed = logSetting.IgnoredChannels.RemoveWhere(ilc => ilc.ChannelId == cid);
            config.LogSetting.IgnoredChannels.RemoveWhere(ilc => ilc.ChannelId == cid);
            if (removed == 0)
            {
                var toAdd = new IgnoredLogChannel {ChannelId = cid};
                logSetting.IgnoredChannels.Add(toAdd);
                config.LogSetting.IgnoredChannels.Add(toAdd);
            }

            uow.SaveChanges();
        }

        return removed > 0;
    }

    private string GetText(IGuild guild, string key, params object[] replacements) => _strings.GetText(key, guild.Id, replacements);


    private string PrettyCurrentTime(IGuild? g)
    {
        var time = DateTime.UtcNow;
        if (g != null)
            time = TimeZoneInfo.ConvertTime(time, _tz.GetTimeZoneOrUtc(g.Id));
        return $"【{time:HH:mm:ss}】";
    }

    private string CurrentTime(IGuild? g)
    {
        var time = DateTime.UtcNow;
        if (g != null)
            time = TimeZoneInfo.ConvertTime(time, _tz.GetTimeZoneOrUtc(g.Id));

        return $"{time:HH:mm:ss}";
    }

    public async Task LogServer(ulong guildId, ulong channelId, bool value)
    {
        await using var uow = _db.GetDbContext();
        var logSetting = uow.LogSettingsFor(guildId).LogSetting;
        GuildLogSettings.AddOrUpdate(guildId, _ => logSetting, (_, _) => logSetting);
        logSetting.LogOtherId =
            logSetting.MessageUpdatedId =
                logSetting.MessageDeletedId =
                    logSetting.UserJoinedId =
                        logSetting.UserLeftId =
                            logSetting.UserBannedId =
                                logSetting.UserUnbannedId =
                                    logSetting.UserUpdatedId =
                                        logSetting.ChannelCreatedId =
                                            logSetting.ChannelDestroyedId =
                                                logSetting.ChannelUpdatedId =
                                                        logSetting.LogVoicePresenceId =
                                                            logSetting.UserMutedId =
                                                                logSetting.LogVoicePresenceTTSId =
                                                                    value ? channelId : null;

        await uow.SaveChangesAsync();
    }

    private Task Client_UserUpdated(SocketUser before, SocketUser uAfter)
    {
        var _ = Task.Run(async () =>
        {
            try
            {
                if (uAfter is not SocketGuildUser guildUser)
                    return;

                var g = guildUser.Guild;

                if (!GuildLogSettings.TryGetValue(g.Id, out var logSetting)
                    || logSetting.UserUpdatedId == null)
                    return;

                ITextChannel logChannel;
                if ((logChannel =
                        await TryGetLogChannel(g, logSetting, LogType.UserUpdated).ConfigureAwait(false)) == null)
                    return;

                var embed = new EmbedBuilder();

                if (before.Username != guildUser.Username)
                {
                    embed.WithTitle($"👥 {GetText(g, "username_changed")}")
                        .WithDescription($"{before.Username}#{before.Discriminator} | {before.Id}")
                        .AddField(fb => fb.WithName("Old Name").WithValue($"{before.Username}").WithIsInline(true))
                        .AddField(fb => fb.WithName("New Name").WithValue($"{guildUser.Username}").WithIsInline(true))
                        .WithFooter(fb => fb.WithText(CurrentTime(g)))
                        .WithOkColor();
                }
                else if (before.AvatarId != guildUser.AvatarId)
                {
                    embed.WithTitle($"👥{GetText(g, "avatar_changed")}")
                        .WithDescription($"{before.Username}#{before.Discriminator} | {before.Id}")
                        .WithFooter(fb => fb.WithText(CurrentTime(g)))
                        .WithOkColor();

                    var bav = before.RealAvatarUrl();
                    if (bav != null && bav.IsAbsoluteUri)
                        embed.WithThumbnailUrl(bav.ToString());

                    var aav = guildUser.RealAvatarUrl();
                    if (aav != null && aav.IsAbsoluteUri)
                        embed.WithImageUrl(aav.ToString());
                }
                else
                {
                    return;
                }

                await logChannel.EmbedAsync(embed).ConfigureAwait(false);
            }
            catch
            {
                // ignored
            }
        });
        return Task.CompletedTask;
    }

    public async Task<bool> Log(ulong gid, ulong? cid, LogType type /*, string options*/)
    {
        ulong? channelId = null;
        await using (var uow = _db.GetDbContext())
        {
            var logSetting = uow.LogSettingsFor(gid).LogSetting;
            GuildLogSettings.AddOrUpdate(gid, _ => logSetting, (_, _) => logSetting);
            channelId = type switch
            {
                LogType.Other => logSetting.LogOtherId = logSetting.LogOtherId == null ? cid : default,
                LogType.MessageUpdated => logSetting.MessageUpdatedId =
                    logSetting.MessageUpdatedId == null ? cid : default,
                LogType.MessageDeleted => logSetting.MessageDeletedId =
                    logSetting.MessageDeletedId == null ? cid : default,
                LogType.UserJoined => logSetting.UserJoinedId = logSetting.UserJoinedId == null ? cid : default,
                LogType.UserLeft => logSetting.UserLeftId = logSetting.UserLeftId == null ? cid : default,
                LogType.UserBanned => logSetting.UserBannedId = logSetting.UserBannedId == null ? cid : default,
                LogType.UserUnbanned => logSetting.UserUnbannedId =
                    logSetting.UserUnbannedId == null ? cid : default,
                LogType.UserUpdated => logSetting.UserUpdatedId = logSetting.UserUpdatedId == null ? cid : default,
                LogType.UserMuted => logSetting.UserMutedId = logSetting.UserMutedId == null ? cid : default,
                LogType.ChannelCreated => logSetting.ChannelCreatedId =
                    logSetting.ChannelCreatedId == null ? cid : default,
                LogType.ChannelDestroyed => logSetting.ChannelDestroyedId =
                    logSetting.ChannelDestroyedId == null ? cid : default,
                LogType.ChannelUpdated => logSetting.ChannelUpdatedId =
                    logSetting.ChannelUpdatedId == null ? cid : default,
                LogType.VoicePresence => logSetting.LogVoicePresenceId =
                    logSetting.LogVoicePresenceId == null ? cid : default,
                LogType.VoicePresenceTts => logSetting.LogVoicePresenceTTSId =
                    logSetting.LogVoicePresenceTTSId == null ? cid : default,
                _ => null
            };

            await uow.SaveChangesAsync();
        }

        return channelId != null;
    }

    private Task Client_UserVoiceStateUpdated_TTS(SocketUser iusr, SocketVoiceState before, SocketVoiceState after)
    {
        var _ = Task.Run(async () =>
        {
            try
            {
                if (iusr is not IGuildUser usr)
                    return;

                var beforeVch = before.VoiceChannel;
                var afterVch = after.VoiceChannel;

                if (beforeVch == afterVch)
                    return;

                if (!GuildLogSettings.TryGetValue(usr.Guild.Id, out var logSetting)
                    || logSetting.LogVoicePresenceTTSId == null)
                    return;

                ITextChannel logChannel;
                if ((logChannel = await TryGetLogChannel(usr.Guild, logSetting, LogType.VoicePresenceTts)
                        .ConfigureAwait(false)) == null)
                    return;

                var str = "";
                if (beforeVch?.Guild == afterVch?.Guild)
                    str = GetText(logChannel.Guild, "log_vc_moved", usr.Username, beforeVch?.Name, afterVch?.Name);
                else if (beforeVch == null)
                    str = GetText(logChannel.Guild, "log_vc_joined", usr.Username, afterVch.Name);
                else if (afterVch == null)
                    str = GetText(logChannel.Guild, "log_vc_left", usr.Username, beforeVch.Name);

                var toDelete = await logChannel.SendMessageAsync(str, true).ConfigureAwait(false);
                toDelete.DeleteAfter(5);
            }
            catch
            {
                // ignored
            }
        });
        return Task.CompletedTask;
    }

    private void MuteCommands_UserMuted(IGuildUser usr, IUser mod, MuteType muteType, string reason)
    {
        var _ = Task.Run(async () =>
        {
            try
            {
                if (!GuildLogSettings.TryGetValue(usr.Guild.Id, out var logSetting)
                    || logSetting.UserMutedId == null)
                    return;

                ITextChannel logChannel;
                if ((logChannel = await TryGetLogChannel(usr.Guild, logSetting, LogType.UserMuted)
                        .ConfigureAwait(false)) == null)
                    return;
                var mutes = "";
                var mutedLocalized = GetText(logChannel.Guild, "muted_sn");
                mutes = muteType switch
                {
                    MuteType.Voice => $"🔇 {GetText(logChannel.Guild, "xmuted_voice", mutedLocalized, mod.ToString())}",
                    MuteType.Chat => $"🔇 {GetText(logChannel.Guild, "xmuted_text", mutedLocalized, mod.ToString())}",
                    MuteType.All =>
                        $"🔇 {GetText(logChannel.Guild, "xmuted_text_and_voice", mutedLocalized, mod.ToString())}",
                    _ => mutes
                };

                var embed = new EmbedBuilder().WithAuthor(eab => eab.WithName(mutes))
                                              .WithTitle($"{usr.Username}#{usr.Discriminator} | {usr.Id}")
                                              .WithFooter(fb => fb.WithText(CurrentTime(usr.Guild)))
                                              .WithOkColor();

                await logChannel.EmbedAsync(embed).ConfigureAwait(false);
            }
            catch
            {
                // ignored
            }
        });
    }

    private void MuteCommands_UserUnmuted(IGuildUser usr, IUser mod, MuteType muteType, string reason)
    {
        var _ = Task.Run(async () =>
        {
            try
            {
                if (!GuildLogSettings.TryGetValue(usr.Guild.Id, out var logSetting)
                    || logSetting.UserMutedId == null)
                    return;

                ITextChannel logChannel;
                if ((logChannel = await TryGetLogChannel(usr.Guild, logSetting, LogType.UserMuted)
                        .ConfigureAwait(false)) == null)
                    return;

                var mutes = "";
                var unmutedLocalized = GetText(logChannel.Guild, "unmuted_sn");
                mutes = muteType switch
                {
                    MuteType.Voice =>
                        $"🔊 {GetText(logChannel.Guild, "xmuted_voice", unmutedLocalized, mod.ToString())}",
                    MuteType.Chat => $"🔊 {GetText(logChannel.Guild, "xmuted_text", unmutedLocalized, mod.ToString())}",
                    MuteType.All =>
                        $"🔊 {GetText(logChannel.Guild, "xmuted_text_and_voice", unmutedLocalized, mod.ToString())}",
                    _ => mutes
                };

                var embed = new EmbedBuilder().WithAuthor(eab => eab.WithName(mutes))
                    .WithTitle($"{usr.Username}#{usr.Discriminator} | {usr.Id}")
                    .WithFooter(fb => fb.WithText($"{CurrentTime(usr.Guild)}"))
                    .WithOkColor();

                if (!string.IsNullOrWhiteSpace(reason))
                    embed.WithDescription(reason);

                await logChannel.EmbedAsync(embed).ConfigureAwait(false);
            }
            catch
            {
                // ignored
            }
        });
    }

    public Task TriggeredAntiProtection(PunishmentAction action, ProtectionType protection,
        params IGuildUser[] users)
    {
        var _ = Task.Run(async () =>
        {
            try
            {
                if (users.Length == 0)
                    return;

                if (!GuildLogSettings.TryGetValue(users.First().Guild.Id, out var logSetting)
                    || logSetting.LogOtherId == null)
                    return;
                ITextChannel logChannel;
                if ((logChannel = await TryGetLogChannel(users.First().Guild, logSetting, LogType.Other)
                        .ConfigureAwait(false)) == null)
                    return;

                var punishment = "";
                switch (action)
                {
                    case PunishmentAction.Mute:
                        punishment = $"🔇 {GetText(logChannel.Guild, "muted_pl").ToUpperInvariant()}";
                        break;
                    case PunishmentAction.Kick:
                        punishment = $"👢 {GetText(logChannel.Guild, "kicked_pl").ToUpperInvariant()}";
                        break;
                    case PunishmentAction.Softban:
                        punishment = $"☣ {GetText(logChannel.Guild, "soft_banned_pl").ToUpperInvariant()}";
                        break;
                    case PunishmentAction.Ban:
                        punishment = $"⛔️ {GetText(logChannel.Guild, "banned_pl").ToUpperInvariant()}";
                        break;
                    case PunishmentAction.RemoveRoles:
                        punishment = $"⛔️ {GetText(logChannel.Guild, "remove_roles_pl").ToUpperInvariant()}";
                        break;
                    case PunishmentAction.ChatMute:
                        break;
                    case PunishmentAction.VoiceMute:
                        break;
                    case PunishmentAction.AddRole:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(action), action, null);
                }

                var embed = new EmbedBuilder().WithAuthor(eab => eab.WithName($"🛡 Anti-{protection}"))
                    .WithTitle($"{GetText(logChannel.Guild, "users")} {punishment}")
                    .WithDescription(string.Join("\n", users.Select(u => u.ToString())))
                    .WithFooter(fb => fb.WithText(CurrentTime(logChannel.Guild)))
                    .WithOkColor();

                await logChannel.EmbedAsync(embed).ConfigureAwait(false);
            }
            catch
            {
                // ignored
            }
        });
        return Task.CompletedTask;
    }

    private static string GetRoleDeletedKey(ulong roleId) => $"role_deleted_{roleId}";

    private Task Client_RoleDeleted(SocketRole socketRole)
    {
        Serilog.Log.Information("Role deleted {RoleId}", socketRole.Id);
        _memoryCache.Set(GetRoleDeletedKey(socketRole.Id),
            true,
            TimeSpan.FromMinutes(5));
        return Task.CompletedTask;
    }

    private bool IsRoleDeleted(ulong roleId)
    {
        var isDeleted = _memoryCache.TryGetValue(GetRoleDeletedKey(roleId), out var _);
        return isDeleted;
    }

    private Task Client_GuildUserUpdated(Cacheable<SocketGuildUser, ulong> cacheable, SocketGuildUser? after)
    {
        var _ = Task.Run(async () =>
        {
            try
            {
                if (!_bot.Ready.Task.IsCompleted)
                    return;
                
                if (!cacheable.HasValue)
                    return;

                if (after is null)
                    return;
                    
                if (!GuildLogSettings.TryGetValue((ulong)cacheable.Value?.Guild.Id, out var logSetting))
                    return;

                ITextChannel logChannel;
                if (logSetting.UserUpdatedId != null &&
                    (logChannel = await TryGetLogChannel(cacheable.Value.Guild, logSetting, LogType.UserUpdated)
                        .ConfigureAwait(false)) != null)
                {
                    var embed = new EmbedBuilder().WithOkColor()
                        .WithFooter(efb => efb.WithText(CurrentTime(cacheable.Value.Guild)))
                        .WithTitle($"{cacheable.Value.Username}#{cacheable.Value.Discriminator} | {cacheable.Id}");
                    if (cacheable.Value.Nickname != after.Nickname)
                    {
                        var channel = logChannel;
                        var channel1 = logChannel;
                        var logChannel1 = logChannel;
                        embed.WithAuthor(eab => eab.WithName($"👥 {GetText(logChannel1.Guild, "nick_change")}"))
                            .AddField(efb =>
                                efb.WithName(GetText(channel.Guild, "old_nick"))
                                    .WithValue($"{cacheable.Value.Nickname}#{cacheable.Value.Discriminator}"))
                            .AddField(efb =>
                                efb.WithName(GetText(channel1.Guild, "new_nick"))
                                    .WithValue($"{after.Nickname}#{after.Discriminator}"));

                        await logChannel.EmbedAsync(embed).ConfigureAwait(false);
                    }
                    else if (!cacheable.Value.Roles.SequenceEqual(after.Roles))
                    {
                        if (cacheable.Value.Roles.Count < after.Roles.Count)
                        {
                            var diffRoles = after.Roles.Where(r => !cacheable.Value.Roles.Contains(r))
                                .Select(r => r.Name);
                            var channel = logChannel;
                            embed.WithAuthor(eab => eab.WithName($"⚔ {GetText(channel.Guild, "user_role_add")}"))
                                .WithDescription(string.Join(", ", diffRoles));

                            await logChannel.EmbedAsync(embed).ConfigureAwait(false);
                        }
                        else if (cacheable.Value.Roles.Count > after.Roles.Count)
                        {
                            await Task.Delay(1000);
                            var diffRoles = cacheable.Value.Roles
                                .Where(r => !after.Roles.Contains(r) && !IsRoleDeleted(r.Id))
                                .Select(r => r.Name)
                                .ToList();

                            if (diffRoles.Any())
                            {
                                var channel = logChannel;
                                embed.WithAuthor(eab =>
                                        eab.WithName($"⚔ {GetText(channel.Guild, "user_role_rem")}"))
                                    .WithDescription(string.Join(", ", diffRoles).SanitizeMentions());

                                await logChannel.EmbedAsync(embed).ConfigureAwait(false);
                            }
                        }
                    }
                }
            }
            catch
            {
                // ignored
            }
        });
        return Task.CompletedTask;
    }

    private Task Client_ChannelUpdated(IChannel cbefore, IChannel cafter)
    {
        var _ = Task.Run(async () =>
        {
            try
            {
                if (cbefore is not IGuildChannel before)
                    return;

                var after = (IGuildChannel) cafter;

                if (!GuildLogSettings.TryGetValue(before.Guild.Id, out var logSetting)
                    || logSetting.ChannelUpdatedId == null
                    || logSetting.IgnoredChannels.Any(ilc => ilc.ChannelId == after.Id))
                    return;

                ITextChannel logChannel;
                if ((logChannel = await TryGetLogChannel(before.Guild, logSetting, LogType.ChannelUpdated)
                        .ConfigureAwait(false)) == null)
                    return;

                var embed = new EmbedBuilder().WithOkColor()
                    .WithFooter(efb => efb.WithText(CurrentTime(before.Guild)));

                var beforeTextChannel = cbefore as ITextChannel;
                var afterTextChannel = cafter as ITextChannel;

                if (before.Name != after.Name)
                    embed.WithTitle($"ℹ️ {GetText(logChannel.Guild, "ch_name_change")}")
                        .WithDescription($"{after} | {after.Id}")
                        .AddField(efb =>
                            efb.WithName(GetText(logChannel.Guild, "ch_old_name")).WithValue(before.Name));
                else if (beforeTextChannel?.Topic != afterTextChannel?.Topic)
                    embed.WithTitle($"ℹ️ {GetText(logChannel.Guild, "ch_topic_change")}")
                        .WithDescription($"{after} | {after.Id}")
                        .AddField(efb =>
                            efb.WithName(GetText(logChannel.Guild, "old_topic"))
                                .WithValue(beforeTextChannel?.Topic ?? "-"))
                        .AddField(efb =>
                            efb.WithName(GetText(logChannel.Guild, "new_topic"))
                                .WithValue(afterTextChannel?.Topic ?? "-"));
                else
                    return;

                await logChannel.EmbedAsync(embed).ConfigureAwait(false);
            }
            catch
            {
                // ignored
            }
        });
        return Task.CompletedTask;
    }

    private Task Client_ChannelDestroyed(IChannel ich)
    {
        var _ = Task.Run(async () =>
        {
            try
            {
                if (ich is not IGuildChannel ch)
                    return;

                if (!GuildLogSettings.TryGetValue(ch.Guild.Id, out var logSetting)
                    || logSetting.ChannelDestroyedId == null
                    || logSetting.IgnoredChannels.Any(ilc => ilc.ChannelId == ch.Id))
                    return;

                ITextChannel logChannel;
                if ((logChannel = await TryGetLogChannel(ch.Guild, logSetting, LogType.ChannelDestroyed)
                        .ConfigureAwait(false)) == null)
                    return;
                string title;
                if (ch is IVoiceChannel)
                    title = GetText(logChannel.Guild, "voice_chan_destroyed");
                else
                    title = GetText(logChannel.Guild, "text_chan_destroyed");

                var audits = await ch.Guild.GetAuditLogsAsync();
                var e = audits.FirstOrDefault(x => x.Action == ActionType.ChannelDeleted);
                await logChannel.EmbedAsync(new EmbedBuilder()
                    .WithOkColor()
                    .WithTitle($"🆕 {title}")
                    .WithDescription($"{ch.Name} | {ch.Id}")
                    .AddField("Yeeted By", e?.User)
                    .WithFooter(efb => efb.WithText(CurrentTime(ch.Guild)))).ConfigureAwait(false);
            }
            catch
            {
                // ignored
            }
        });
        return Task.CompletedTask;
    }

    private Task Client_ChannelCreated(IChannel ich)
    {
        var _ = Task.Run(async () =>
        {
            try
            {
                if (ich is not IGuildChannel ch)
                    return;

                if (!GuildLogSettings.TryGetValue(ch.Guild.Id, out var logSetting)
                    || logSetting.ChannelCreatedId == null)
                    return;

                ITextChannel logChannel;
                if ((logChannel = await TryGetLogChannel(ch.Guild, logSetting, LogType.ChannelCreated)
                        .ConfigureAwait(false)) == null)
                    return;
                string title;
                if (ch is IVoiceChannel)
                    title = GetText(logChannel.Guild, "voice_chan_created");
                else
                    title = GetText(logChannel.Guild, "text_chan_created");

                await logChannel.EmbedAsync(new EmbedBuilder()
                    .WithOkColor()
                    .WithTitle($"🆕 {title}")
                    .WithDescription($"{ch.Name} | {ch.Id}")
                    .WithFooter(efb => efb.WithText(CurrentTime(ch.Guild)))).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // ignored
            }
        });
        return Task.CompletedTask;
    }

    private Task Client_UserVoiceStateUpdated(SocketUser iusr, SocketVoiceState before, SocketVoiceState after)
    {
        var _ = Task.Run(async () =>
        {
            try
            {
                if (iusr is not IGuildUser usr || usr.IsBot)
                    return;

                var beforeVch = before.VoiceChannel;
                var afterVch = after.VoiceChannel;

                if (beforeVch == afterVch)
                    return;

                if (!GuildLogSettings.TryGetValue(usr.Guild.Id, out var logSetting)
                    || logSetting.LogVoicePresenceId == null)
                    return;

                ITextChannel logChannel;
                if ((logChannel = await TryGetLogChannel(usr.Guild, logSetting, LogType.VoicePresence)
                        .ConfigureAwait(false)) == null)
                    return;

                string str = null;
                if (beforeVch?.Guild == afterVch?.Guild)
                    str =
                        $"🎙{Format.Code(PrettyCurrentTime(usr.Guild))}{GetText(logChannel.Guild, "user_vmoved", $"👤{Format.Bold($"{usr.Username}#{usr.Discriminator}")}", Format.Bold(beforeVch?.Name ?? ""), Format.Bold(afterVch?.Name ?? ""))}";
                else if (beforeVch == null)
                    str =
                        $"🎙{Format.Code(PrettyCurrentTime(usr.Guild))}{GetText(logChannel.Guild, "user_vjoined", $"👤{Format.Bold($"{usr.Username}#{usr.Discriminator}")}", Format.Bold(afterVch.Name ?? ""))}";
                else if (afterVch == null)
                    str =
                        $"🎙{Format.Code(PrettyCurrentTime(usr.Guild))}{GetText(logChannel.Guild, "user_vleft", $"👤{Format.Bold($"{usr.Username}#{usr.Discriminator}")}", Format.Bold(beforeVch.Name ?? ""))}";

                if (!string.IsNullOrWhiteSpace(str))
                    PresenceUpdates.AddOrUpdate(logChannel, new List<string> {str}, (_, list) =>
                    {
                        list.Add(str);
                        return list;
                    });
            }
            catch
            {
                // ignored
            }
        });
        return Task.CompletedTask;
    }


    private Task Client_UserLeft(SocketGuild guild, SocketUser user)
    {
        var _ = Task.Run(async () =>
        {
            try
            {
                if (user is not SocketGuildUser usr) return;
                if (!GuildLogSettings.TryGetValue(guild.Id, out var logSetting)
                    || logSetting.UserLeftId == null)
                    return;

                ITextChannel logChannel;
                if ((logChannel = await TryGetLogChannel(guild, logSetting, LogType.UserLeft)
                        .ConfigureAwait(false)) == null)
                    return;
                var embed = new EmbedBuilder()
                    .WithOkColor()
                    .WithTitle($"❌ {GetText(logChannel.Guild, "user_left")}")
                    .WithDescription(usr.ToString())
                    .AddField(efb => efb.WithName("Id").WithValue(usr.Id.ToString()))
                    .AddField("Roles", string.Join("|", usr.GetRoles().Select(x => x.Mention)))
                    .AddField("Time Stayed:", (usr.JoinedAt - DateTime.Now).Value.Humanize())
                    .WithFooter(efb => efb.WithText(CurrentTime(usr.Guild)));

                if (Uri.IsWellFormedUriString(usr.GetAvatarUrl(), UriKind.Absolute))
                    embed.WithThumbnailUrl(usr.GetAvatarUrl());

                await logChannel.EmbedAsync(embed).ConfigureAwait(false);
            }
            catch
            {
                // ignored
            }
        });
        return Task.CompletedTask;
    }

    private Task Client_UserJoined(IGuildUser usr)
    {
        var _ = Task.Run(async () =>
        {
            try
            {
                if (!GuildLogSettings.TryGetValue(usr.Guild.Id, out var logSetting)
                    || logSetting.UserJoinedId == null)
                    return;

                ITextChannel logChannel;
                if ((logChannel = await TryGetLogChannel(usr.Guild, logSetting, LogType.UserJoined)
                        .ConfigureAwait(false)) == null)
                    return;

                var embed = new EmbedBuilder()
                    .WithOkColor()
                    .WithTitle($"✅ {GetText(logChannel.Guild, "user_joined")}")
                    .WithDescription($"{usr.Mention} `{usr}`")
                    .AddField(efb => efb.WithName("Id").WithValue(usr.Id.ToString()))
                    .AddField(fb =>
                        fb.WithName(GetText(logChannel.Guild, "joined_server"))
                            .WithValue($"{usr.JoinedAt?.ToString("dd.MM.yyyy HH:mm") ?? "?"}").WithIsInline(true))
                    .AddField(fb =>
                        fb.WithName(GetText(logChannel.Guild, "joined_discord"))
                            .WithValue($"{usr.CreatedAt:dd.MM.yyyy HH:mm}").WithIsInline(true))
                    .WithFooter(efb => efb.WithText(CurrentTime(usr.Guild)));

                if (Uri.IsWellFormedUriString(usr.GetAvatarUrl(), UriKind.Absolute))
                    embed.WithThumbnailUrl(usr.GetAvatarUrl());

                await logChannel.EmbedAsync(embed).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // ignored
            }
        });
        return Task.CompletedTask;
    }

    private Task Client_UserUnbanned(IUser usr, IGuild guild)
    {
        var _ = Task.Run(async () =>
        {
            try
            {
                if (!GuildLogSettings.TryGetValue(guild.Id, out var logSetting)
                    || logSetting.UserUnbannedId == null)
                    return;

                ITextChannel logChannel;
                if ((logChannel = await TryGetLogChannel(guild, logSetting, LogType.UserUnbanned)
                        .ConfigureAwait(false)) == null)
                    return;
                var embed = new EmbedBuilder()
                    .WithOkColor()
                    .WithTitle($"♻️ {GetText(logChannel.Guild, "user_unbanned")}")
                    .WithDescription(usr.ToString())
                    .AddField(efb => efb.WithName("Id").WithValue(usr.Id.ToString()))
                    .WithFooter(efb => efb.WithText(CurrentTime(guild)));

                if (Uri.IsWellFormedUriString(usr.GetAvatarUrl(), UriKind.Absolute))
                    embed.WithThumbnailUrl(usr.GetAvatarUrl());

                await logChannel.EmbedAsync(embed).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // ignored
            }
        });
        return Task.CompletedTask;
    }

    private Task Client_UserBanned(IUser usr, IGuild guild)
    {
        var _ = Task.Run(async () =>
        {
            try
            {
                if (!GuildLogSettings.TryGetValue(guild.Id, out var logSetting)
                    || logSetting.UserBannedId == null)
                    return;
                var bannedby = (await guild.GetAuditLogsAsync(actionType: ActionType.Ban)).FirstOrDefault();
                ITextChannel logChannel;
                if ((logChannel =
                        await TryGetLogChannel(guild, logSetting, LogType.UserBanned).ConfigureAwait(false)) ==
                    null)
                    return;
                var embed = new EmbedBuilder()
                    .WithOkColor()
                    .WithTitle($"🚫 {GetText(logChannel.Guild, "user_banned")}")
                    .WithDescription(usr.ToString());

                if (bannedby != null)
                    embed
                    .AddField("Banned by", bannedby.User)
                    .AddField("Reason", bannedby.Reason == null ? bannedby.Reason : "None");

                embed.AddField(efb => efb.WithName("Id").WithValue(usr.Id.ToString()))
                    .WithFooter(efb => efb.WithText(CurrentTime(guild)));

                var avatarUrl = usr.GetAvatarUrl();

                if (Uri.IsWellFormedUriString(avatarUrl, UriKind.Absolute))
                    embed.WithThumbnailUrl(usr.GetAvatarUrl());

                await logChannel.EmbedAsync(embed).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // ignored
            }
        });
        return Task.CompletedTask;
    }

    private Task Client_BulkDelete(IReadOnlyCollection<Cacheable<IMessage, ulong>> messages,
        Cacheable<IMessageChannel, ulong> channel)
    {
        var _ = Task.Run(async () =>
        {
            if (channel.Value is not ITextChannel chan)
                return;

            if (!GuildLogSettings.TryGetValue(chan.Guild.Id, out var logSetting)
                || logSetting.MessageDeletedId == null
                || logSetting.IgnoredChannels.Any(ilc => ilc.ChannelId == channel.Id))
                return;

            ITextChannel logChannel;
            if ((logChannel = await TryGetLogChannel(chan.Guild, logSetting, LogType.MessageDeleted)
                    .ConfigureAwait(false)) == null)
                return;

            var toSend = new List<IUserMessage>();
            foreach (var message in messages)
                if ((message.HasValue ? message.Value : null) is IUserMessage msg && !msg.IsAuthor(_client) &&
                    !_ignoreMessageIds.Contains(msg.Id))
                    toSend.Add(msg);
            var count = toSend.Count;

            if (count == 1)
                return;

            while (toSend.Any())
            {
                var toBatch = toSend.Take(100);
                foreach (var group in toBatch.Chunk(20))
                {
                    var eb = new EmbedBuilder().WithOkColor();
                    eb.WithTitle($"🗑 {count} Messages bulk deleted in {channel.Value.Name}");
                    eb.WithDescription(string.Join("\n",
                        group.Select(x => $"{x.Author}: {x.Content}".Truncate(202))));
                    await logChannel.SendMessageAsync(embed: eb.Build());
                }

                toSend = toSend.Skip(100).ToList();
                await Task.Delay(1000);
            }
        });

        return Task.CompletedTask;
    }

    private Task Client_MessageDeleted(Cacheable<IMessage, ulong> optMsg, Cacheable<IMessageChannel, ulong> ch)
    {
        var _ = Task.Run(async () =>
        {
            try
            {
                if ((optMsg.HasValue ? optMsg.Value : null) is not IUserMessage msg || msg.IsAuthor(_client))
                    return;

                if (_ignoreMessageIds.Contains(msg.Id))
                    return;

                if (ch.Value is not ITextChannel channel)
                    return;

                if (!GuildLogSettings.TryGetValue(channel.Guild.Id, out var logSetting)
                    || logSetting.MessageDeletedId == null
                    || logSetting.IgnoredChannels.Any(ilc => ilc.ChannelId == channel.Id))
                    return;

                ITextChannel logChannel;
                if ((logChannel = await TryGetLogChannel(channel.Guild, logSetting, LogType.MessageDeleted)
                        .ConfigureAwait(false)) == null || logChannel.Id == msg.Id)
                    return;

                var resolvedMessage = msg.Resolve(TagHandling.FullName);
                var embed = new EmbedBuilder()
                    .WithOkColor()
                    .WithTitle($"🗑 {GetText(logChannel.Guild, "msg_del", ((ITextChannel)msg.Channel).Name)}")
                    .WithDescription(msg.Author.ToString())
                    .AddField(efb =>
                        efb.WithName(GetText(logChannel.Guild, "content"))
                            .WithValue(string.IsNullOrWhiteSpace(resolvedMessage) ? "-" : resolvedMessage)
                            .WithIsInline(false))
                    .AddField(efb => efb.WithName("Id").WithValue(msg.Id.ToString()).WithIsInline(false))
                    .WithFooter(efb => efb.WithText(CurrentTime(channel.Guild)));
                if (msg.Attachments.Any())
                    embed.AddField(efb =>
                        efb.WithName(GetText(logChannel.Guild, "attachments"))
                            .WithValue(string.Join(", ", msg.Attachments.Select(a => a.Url))).WithIsInline(false));

                await logChannel.EmbedAsync(embed).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // ignored
            }
        });
        return Task.CompletedTask;
    }

    private Task Client_MessageUpdated(Cacheable<IMessage, ulong> optmsg, SocketMessage imsg2,
        ISocketMessageChannel ch)
    {
        var _ = Task.Run(async () =>
        {
            try
            {
                if (imsg2 is not IUserMessage after || after.IsAuthor(_client))
                    return;

                if ((optmsg.HasValue ? optmsg.Value : null) is not IUserMessage before)
                    return;

                if (ch is not ITextChannel channel)
                    return;

                if (before.Content == after.Content)
                    return;

                if (before.Author.IsBot)
                    return;

                if (!GuildLogSettings.TryGetValue(channel.Guild.Id, out var logSetting)
                    || logSetting.MessageUpdatedId == null
                    || logSetting.IgnoredChannels.Any(ilc => ilc.ChannelId == channel.Id))
                    return;

                ITextChannel logChannel;
                if ((logChannel = await TryGetLogChannel(channel.Guild, logSetting, LogType.MessageUpdated)
                        .ConfigureAwait(false)) == null || logChannel.Id == after.Channel.Id)
                    return;

                var embed = new EmbedBuilder()
                    .WithOkColor()
                    .WithTitle($"📝 {GetText(logChannel.Guild, "msg_update", ((ITextChannel)after.Channel).Name)}")
                    .WithDescription(after.Author.ToString())
                    .AddField(efb =>
                        efb.WithName(GetText(logChannel.Guild, "old_msg"))
                            .WithValue(string.IsNullOrWhiteSpace(before.Content)
                                ? "-"
                                : before.Resolve(TagHandling.FullName)).WithIsInline(false))
                    .AddField(efb =>
                        efb.WithName(GetText(logChannel.Guild, "new_msg"))
                            .WithValue(string.IsNullOrWhiteSpace(after.Content)
                                ? "-"
                                : after.Resolve(TagHandling.FullName)).WithIsInline(false))
                    .AddField(efb => efb.WithName("Id").WithValue(after.Id.ToString()).WithIsInline(false))
                    .WithFooter(efb => efb.WithText(CurrentTime(channel.Guild)));

                await logChannel.EmbedAsync(embed).ConfigureAwait(false);
            }
            catch
            {
                // ignored
            }
        });
        return Task.CompletedTask;
    }

    private async Task<ITextChannel> TryGetLogChannel(IGuild guild, LogSetting logSetting, LogType logChannelType)
    {
        var id = logChannelType switch
        {
            LogType.Other => logSetting.LogOtherId,
            LogType.MessageUpdated => logSetting.MessageUpdatedId,
            LogType.MessageDeleted => logSetting.MessageDeletedId,
            LogType.UserJoined => logSetting.UserJoinedId,
            LogType.UserLeft => logSetting.UserLeftId,
            LogType.UserBanned => logSetting.UserBannedId,
            LogType.UserUnbanned => logSetting.UserUnbannedId,
            LogType.UserUpdated => logSetting.UserUpdatedId,
            LogType.ChannelCreated => logSetting.ChannelCreatedId,
            LogType.ChannelDestroyed => logSetting.ChannelDestroyedId,
            LogType.ChannelUpdated => logSetting.ChannelUpdatedId,
            LogType.VoicePresence => logSetting.LogVoicePresenceId,
            LogType.VoicePresenceTts => logSetting.LogVoicePresenceTTSId,
            LogType.UserMuted => logSetting.UserMutedId,
            _ => null
        };

        if (id is null or 0)
        {
            UnsetLogSetting(guild.Id, logChannelType);
            return null;
        }

        var channel = await guild.GetTextChannelAsync(id.Value).ConfigureAwait(false);

        if (channel == null)
        {
            UnsetLogSetting(guild.Id, logChannelType);
            return null;
        }

        return channel;
    }

    private void UnsetLogSetting(ulong guildId, LogType logChannelType)
    {
        using var uow = _db.GetDbContext();
        var newLogSetting = uow.LogSettingsFor(guildId).LogSetting;
        switch (logChannelType)
        {
            case LogType.Other:
                newLogSetting.LogOtherId = null;
                break;
            case LogType.MessageUpdated:
                newLogSetting.MessageUpdatedId = null;
                break;
            case LogType.MessageDeleted:
                newLogSetting.MessageDeletedId = null;
                break;
            case LogType.UserJoined:
                newLogSetting.UserJoinedId = null;
                break;
            case LogType.UserLeft:
                newLogSetting.UserLeftId = null;
                break;
            case LogType.UserBanned:
                newLogSetting.UserBannedId = null;
                break;
            case LogType.UserUnbanned:
                newLogSetting.UserUnbannedId = null;
                break;
            case LogType.UserUpdated:
                newLogSetting.UserUpdatedId = null;
                break;
            case LogType.UserMuted:
                newLogSetting.UserMutedId = null;
                break;
            case LogType.ChannelCreated:
                newLogSetting.ChannelCreatedId = null;
                break;
            case LogType.ChannelDestroyed:
                newLogSetting.ChannelDestroyedId = null;
                break;
            case LogType.ChannelUpdated:
                newLogSetting.ChannelUpdatedId = null;
                break;
            case LogType.VoicePresence:
                newLogSetting.LogVoicePresenceId = null;
                break;
            case LogType.VoicePresenceTts:
                newLogSetting.LogVoicePresenceTTSId = null;
                break;
        }

        GuildLogSettings.AddOrUpdate(guildId, newLogSetting, (_, _) => newLogSetting);
        uow.SaveChanges();
    }
}