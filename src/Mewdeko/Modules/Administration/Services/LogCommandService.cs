using Humanizer;
using Mewdeko.Common.Collections;
using Mewdeko.Modules.Administration.Common;
using Mewdeko.Modules.Moderation.Services;
using Mewdeko.Services.strings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Embed = Discord.Embed;

namespace Mewdeko.Modules.Administration.Services;

public class LogCommandService : INService
{
    public enum LogType
    {
        AvatarUpdated,
        ChannelCreated,
        ChannelDestroyed,
        ChannelUpdated,
        EventCreated,
        MessageDeleted,
        MessageUpdated,
        NicknameUpdated,
        Other,
        RoleCreated,
        RoleDeleted,
        RoleUpdated,
        ServerUpdated,
        ThreadCreated,
        ThreadDeleted,
        ThreadUpdated,
        UserBanned,
        UserJoined,
        UserLeft,
        UserMuted,
        UserRoleAdded,
        UserRoleRemoved,
        UserUnbanned,
        UserUpdated,
        UsernameUpdated,
        VoicePresence,
        VoicePresenceTts,
    }

    public enum LogCategoryTypes
    {
        All,
        Channel,
        Messages,
        Moderation,
        None,
        Roles,
        Server,
        Threads,
        Users,
        
    }
    private readonly DiscordSocketClient _client;
    private readonly DbService _db;
    private readonly ConcurrentHashSet<ulong> _ignoreMessageIds = new();
    private readonly IMemoryCache _memoryCache;
    private readonly IBotStrings _strings;
    private readonly Mewdeko _bot;
    private readonly GuildSettingsService _guildSettings;

    private readonly GuildTimezoneService _tz;

    public LogCommandService(DiscordSocketClient client, IBotStrings strings,
        DbService db, MuteService mute, ProtectionService prot, GuildTimezoneService tz,
        IMemoryCache memoryCache, Mewdeko bot,
        GuildSettingsService guildSettings)
    {
        _bot = bot;
        _guildSettings = guildSettings;
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
        _client.UserUpdated += Client_UserUpdated;
        _client.ChannelCreated += Client_ChannelCreated;
        _client.ChannelDestroyed += Client_ChannelDestroyed;
        _client.ChannelUpdated += Client_ChannelUpdated;
        _client.RoleDeleted += Client_RoleDeleted;

        mute.UserMuted += MuteCommands_UserMuted;
        mute.UserUnmuted += MuteCommands_UserUnmuted;
        //_client.ThreadCreated += ThreadCreated;
        prot.OnAntiProtectionTriggered += TriggeredAntiProtection;
        _client.GuildMemberUpdated += AddNickname;
        _client.UserUpdated += AddUsername;

        _ = RunCacheClear();
    }

    public async Task RunCacheClear()
    {
        var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        while (await timer.WaitForNextTickAsync())
        {
            _ignoreMessageIds.Clear();
        }
    }
    public ConcurrentDictionary<ulong, LogSetting> GuildLogSettings { get; }

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
                var toAdd = new IgnoredLogChannel { ChannelId = cid };
                logSetting.IgnoredChannels.Add(toAdd);
                config.LogSetting.IgnoredChannels.Add(toAdd);
            }

            uow.SaveChanges();
        }

        return removed > 0;
    }
    private Task AddNickname(Cacheable<SocketGuildUser, ulong> unused, SocketGuildUser socketGuildUser)
    {
        _ = Task.Factory.StartNew(async () =>
        {
            await using var uow = _db.GetDbContext();
            uow.Nicknames.Add(new Nicknames
            {
                GuildId = socketGuildUser.Guild.Id,
                UserId = socketGuildUser.Id,
                Nickname = socketGuildUser.Nickname
            });
            await uow.SaveChangesAsync();
        }, TaskCreationOptions.LongRunning);
        return Task.CompletedTask;
    }

    public Task AddUsername(SocketUser socketUser, SocketUser user)
    {
        _ = Task.Factory.StartNew(async () =>
        {
            await using var uow = _db.GetDbContext();
            uow.Usernames.Add(new Usernames
            {
                UserId = user.Id,
                Username = user.ToString()
            });
            await uow.SaveChangesAsync();
        }, TaskCreationOptions.LongRunning);
        return Task.CompletedTask;
    }
    private string? GetText(IGuild guild, string? key, params object?[] replacements) => _strings.GetText(key, guild.Id, replacements);

    private string CurrentTime(IGuild? g)
    {
        var time = DateTime.UtcNow;
        if (g != null)
            time = TimeZoneInfo.ConvertTime(time, _tz.GetTimeZoneOrUtc(g.Id));

        return $"{time:HH:mm:ss}";
    }

    public async Task SetLogChannel(ulong guildId, ulong channelId, LogType type)
    {
        await using var uow = _db.GetDbContext();
        var logSetting = uow.LogSettingsFor(guildId).LogSetting;
        GuildLogSettings.AddOrUpdate(guildId, _ => logSetting, (_, _) => logSetting);
        switch (type)
        {
            case LogType.Other:
                logSetting.LogOtherId = channelId;
                break;
            case LogType.EventCreated:
                logSetting.EventCreatedId = channelId;
                break;
            case LogType.RoleUpdated:
                logSetting.RoleUpdatedId = channelId;
                break;
            case LogType.RoleCreated:
                logSetting.RoleCreatedId = channelId;
                break;
            case LogType.ServerUpdated:
                logSetting.ServerUpdatedId = channelId;
                break;
            case LogType.ThreadCreated:
                logSetting.ThreadCreatedId = channelId;
                break;
            case LogType.UserRoleAdded:
                logSetting.UserRoleAddedId = channelId;
                break;
            case LogType.UserRoleRemoved:
                logSetting.UserRoleRemovedId = channelId;
                break;
            case LogType.UsernameUpdated:
                logSetting.UsernameUpdatedId = channelId;
                break;
            case LogType.NicknameUpdated:
                logSetting.NicknameUpdatedId = channelId;
                break;
            case LogType.ThreadDeleted:
                logSetting.ThreadDeletedId = channelId;
                break;
            case LogType.ThreadUpdated:
                logSetting.ThreadUpdatedId = channelId;
                break;
            case LogType.MessageUpdated:
                logSetting.MessageUpdatedId = channelId;
                break;
            case LogType.MessageDeleted:
                logSetting.MessageDeletedId = channelId;
                break;
            case LogType.UserJoined:
                logSetting.UserJoinedId = channelId;
                break;
            case LogType.UserLeft:
                logSetting.UserLeftId = channelId;
                break;
            case LogType.UserBanned:
                logSetting.UserBannedId = channelId;
                break;
            case LogType.UserUnbanned:
                logSetting.UserUnbannedId = channelId;
                break;
            case LogType.UserUpdated:
                logSetting.UserUpdatedId = channelId;
                break;
            case LogType.ChannelCreated:
                logSetting.ChannelCreatedId = channelId;
                break;
            case LogType.ChannelDestroyed:
                logSetting.ChannelDestroyedId = channelId;
                break;
            case LogType.ChannelUpdated:
                logSetting.ChannelUpdatedId = channelId;
                break;
            case LogType.VoicePresence:
                logSetting.LogVoicePresenceId = channelId;
                break;
            case LogType.VoicePresenceTts:
                logSetting.LogVoicePresenceTTSId = channelId;
                break;
            case LogType.UserMuted:
                logSetting.UserMutedId = channelId;
                break;
        }

        await uow.SaveChangesAsync();
    }
    public async Task LogSetByType(ulong guildId, ulong channelId, LogCategoryTypes categoryTypes)
    {
        await using var uow = _db.GetDbContext();
        var logSetting = uow.LogSettingsFor(guildId).LogSetting;
        GuildLogSettings.AddOrUpdate(guildId, _ => logSetting, (_, _) => logSetting);
        switch (categoryTypes)
        {
            case LogCategoryTypes.All:
                logSetting.AvatarUpdatedId = channelId;
                logSetting.ChannelCreatedId = channelId;
                logSetting.ChannelDestroyedId = channelId;
                logSetting.ChannelUpdatedId = channelId;
                logSetting.EventCreatedId = channelId;
                logSetting.LogOtherId = channelId;
                logSetting.MessageDeletedId = channelId;
                logSetting.MessageUpdatedId = channelId;
                logSetting.NicknameUpdatedId = channelId;
                logSetting.RoleCreatedId = channelId;
                logSetting.RoleDeletedId = channelId;
                logSetting.RoleUpdatedId = channelId;
                logSetting.ServerUpdatedId = channelId;
                logSetting.ThreadCreatedId = channelId;
                logSetting.ThreadDeletedId = channelId;
                logSetting.ThreadUpdatedId = channelId;
                logSetting.UserBannedId = channelId;
                logSetting.UserJoinedId = channelId;
                logSetting.UserLeftId = channelId;
                logSetting.UserMutedId = channelId;
                logSetting.UsernameUpdatedId = channelId;
                logSetting.UserUnbannedId = channelId;
                logSetting.UserUpdatedId = channelId;
                logSetting.LogUserPresenceId = channelId;
                logSetting.LogVoicePresenceId = channelId;
                logSetting.UserRoleAddedId = channelId;
                logSetting.UserRoleRemovedId = channelId;
                logSetting.LogVoicePresenceTTSId = channelId;
                break;
            case LogCategoryTypes.Users:
                logSetting.NicknameUpdatedId = channelId;
                logSetting.AvatarUpdatedId = channelId;
                logSetting.UsernameUpdatedId = channelId;
                logSetting.UserRoleAddedId = channelId;
                logSetting.UserRoleRemovedId = channelId;
                logSetting.LogVoicePresenceId = channelId;
                break;
            case LogCategoryTypes.Threads:
                logSetting.ThreadCreatedId = channelId;
                logSetting.ThreadDeletedId = channelId;
                logSetting.ThreadUpdatedId = channelId;
                break;
            case LogCategoryTypes.Roles:
                logSetting.RoleCreatedId = channelId;
                logSetting.RoleDeletedId = channelId;
                logSetting.RoleUpdatedId = channelId;
                break;
            case LogCategoryTypes.Server:
                logSetting.ServerUpdatedId = channelId;
                logSetting.EventCreatedId = channelId;
                break;
            case LogCategoryTypes.Channel:
                logSetting.ChannelUpdatedId = channelId;
                logSetting.ChannelCreatedId = channelId;
                logSetting.ChannelDestroyedId = channelId;
                break;
            case LogCategoryTypes.Messages:
                logSetting.MessageDeletedId = channelId;
                logSetting.MessageUpdatedId = channelId;
                break;
            case LogCategoryTypes.Moderation:
                logSetting.UserMutedId = channelId;
                logSetting.UserBannedId = channelId;
                logSetting.UserUnbannedId = channelId;
                break;
            case LogCategoryTypes.None:
                logSetting.AvatarUpdatedId = 0;
                logSetting.ChannelCreatedId = 0;
                logSetting.ChannelDestroyedId = 0;
                logSetting.ChannelUpdatedId = 0;
                logSetting.EventCreatedId = 0;
                logSetting.LogOtherId = 0;
                logSetting.MessageDeletedId = 0;
                logSetting.MessageUpdatedId = 0;
                logSetting.NicknameUpdatedId = 0;
                logSetting.RoleCreatedId = 0;
                logSetting.RoleDeletedId = 0;
                logSetting.RoleUpdatedId = 0;
                logSetting.ServerUpdatedId = 0;
                logSetting.ThreadCreatedId = 0;
                logSetting.ThreadDeletedId = 0;
                logSetting.ThreadUpdatedId = 0;
                logSetting.UserBannedId = 0;
                logSetting.UserJoinedId = 0;
                logSetting.UserLeftId = 0;
                logSetting.UserMutedId = 0;
                logSetting.UsernameUpdatedId = 0;
                logSetting.UserUnbannedId = 0;
                logSetting.UserUpdatedId = 0;
                logSetting.LogUserPresenceId = 0;
                logSetting.LogVoicePresenceId = 0;
                logSetting.UserRoleAddedId = 0;
                logSetting.UserRoleRemovedId = 0;
                logSetting.LogVoicePresenceTTSId = 0;
                break;
        }

        await uow.SaveChangesAsync();
    }

    private Task Client_UserUpdated(SocketUser before, SocketUser uAfter)
    {
        _ = Task.Factory.StartNew(async () =>
        {
            try
            {
                await using var uow = _db.GetDbContext();
                if (uAfter is not SocketGuildUser guildUser)
                    return;

                var g = guildUser.Guild;

                if (!GuildLogSettings.TryGetValue(g.Id, out var logSetting)
                    || logSetting.UserUpdatedId == null)
                {
                    return;
                }

                ITextChannel logChannel;
                if ((logChannel =
                        await TryGetLogChannel(g, logSetting, LogType.UserUpdated).ConfigureAwait(false)) == null)
                {
                    return;
                }

                var embeds = new List<Embed>();

                if (before.ToString() != guildUser.ToString())
                {
                    var user = uow.GetOrCreateUser(guildUser);
                    embeds.Add(new EmbedBuilder().WithTitle($"👥 {GetText(g, "username_changed")}")
                        .WithTitle($"{before.Username}#{before.Discriminator} | {before.Id}")
                        .WithDescription($"**Old Username**\n=> {before}\n**New Username**\n=> {guildUser}\n**Times Changed**\n=> {uow.DiscordUser.GetUsernames(guildUser.Id).Count+1}\n**Date Changed**\n=>{TimestampTag.FromDateTime(DateTime.UtcNow)}")
                        .WithOkColor().Build());
                    var names = user.Usernames.Split("@").ToList();
                    names.Add(guildUser.ToString());
                    user.Usernames = string.Join("@", names);
                    uow.DiscordUser.Update(user);
                    await uow.SaveChangesAsync();
                }
                else if (before.AvatarId != guildUser.AvatarId)
                {
                    var bav = before.RealAvatarUrl();
                    embeds.Add(new EmbedBuilder().WithTitle($"👥{GetText(g, "avatar_changed")}")
                        .WithDescription($"{before.Username}#{before.Discriminator} | {before.Id}")
                        .AddField("Old Avatar", "_ _")
                        .WithImageUrl(bav.ToString())
                        .WithFooter(fb => fb.WithText(CurrentTime(g)))
                        .WithOkColor().Build());

                    var aav = guildUser.RealAvatarUrl();
                    embeds.Add(new EmbedBuilder().AddField("New Avatar", "_ _").WithImageUrl(aav.ToString()).WithOkColor().Build());
                }
                else
                {
                    return;
                }

                await logChannel.SendMessageAsync(embeds: embeds.ToArray()).ConfigureAwait(false);
            }
            catch
            {
                // ignored
            }
        }, TaskCreationOptions.LongRunning);
        return Task.CompletedTask;
    }

    
    public async Task UpdateCommandLogChannel(IGuild guild, ulong id)
    {
        await using var uow = _db.GetDbContext();
        var gc = uow.ForGuildId(guild.Id, set => set);
        gc.CommandLogChannel = id;
        await uow.SaveChangesAsync();
        _guildSettings.UpdateGuildConfig(guild.Id, gc);
    }
    private Task Client_UserVoiceStateUpdated_TTS(SocketUser iusr, SocketVoiceState before, SocketVoiceState after)
    {
        _ = Task.Factory.StartNew(async () =>
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
                {
                    return;
                }

                ITextChannel logChannel;
                if ((logChannel = await TryGetLogChannel(usr.Guild, logSetting, LogType.VoicePresenceTts)
                        .ConfigureAwait(false)) == null)
                {
                    return;
                }

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
        }, TaskCreationOptions.LongRunning);
        return Task.CompletedTask;
    }

    private void MuteCommands_UserMuted(IGuildUser usr, IUser mod, MuteType muteType, string reason) =>
        _ = Task.Factory.StartNew(async () =>
        {
            try
            {
                if (!GuildLogSettings.TryGetValue(usr.Guild.Id, out var logSetting)
                    || logSetting.UserMutedId == null)
                {
                    return;
                }

                ITextChannel logChannel;
                if ((logChannel = await TryGetLogChannel(usr.Guild, logSetting, LogType.UserMuted)
                        .ConfigureAwait(false)) == null)
                {
                    return;
                }

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
        }, TaskCreationOptions.LongRunning);

    private void MuteCommands_UserUnmuted(IGuildUser usr, IUser mod, MuteType muteType, string reason) =>
        _ = Task.Factory.StartNew(async () =>
        {
            try
            {
                if (!GuildLogSettings.TryGetValue(usr.Guild.Id, out var logSetting)
                    || logSetting.UserMutedId == null)
                {
                    return;
                }

                ITextChannel logChannel;
                if ((logChannel = await TryGetLogChannel(usr.Guild, logSetting, LogType.UserMuted)
                        .ConfigureAwait(false)) == null)
                {
                    return;
                }

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
        }, TaskCreationOptions.LongRunning);

    public Task TriggeredAntiProtection(PunishmentAction action, ProtectionType protection,
        params IGuildUser[] users)
    {
        _ = Task.Factory.StartNew(async () =>
        {
            try
            {
                if (users.Length == 0)
                    return;

                if (!GuildLogSettings.TryGetValue(users.First().Guild.Id, out var logSetting)
                    || logSetting.LogOtherId == null)
                {
                    return;
                }

                ITextChannel logChannel;
                if ((logChannel = await TryGetLogChannel(users.First().Guild, logSetting, LogType.Other)
                        .ConfigureAwait(false)) == null)
                {
                    return;
                }

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
        }, TaskCreationOptions.LongRunning);
        return Task.CompletedTask;
    }

    private static string GetRoleDeletedKey(ulong roleId) => $"role_deleted_{roleId}";

    private Task Client_RoleDeleted(SocketRole socketRole)
    {
        _ = Task.Factory.StartNew(() =>
        {
#if DEBUG
            Log.Information("Role deleted {RoleId}", socketRole.Id);
#endif
            _memoryCache.Set(GetRoleDeletedKey(socketRole.Id), true, TimeSpan.FromMinutes(5));
        }, TaskCreationOptions.LongRunning);
        return Task.CompletedTask;
    }

    private bool IsRoleDeleted(ulong roleId) 
        => _memoryCache.TryGetValue(GetRoleDeletedKey(roleId), out _);

    private Task Client_GuildUserUpdated(Cacheable<SocketGuildUser, ulong> cacheable, SocketGuildUser? after)
    {
        _ = Task.Factory.StartNew(async () =>
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
                        .WithTitle($"{cacheable.Value.Username}#{cacheable.Value.Discriminator} | {cacheable.Id}");
                    if (cacheable.Value.Nickname != after.Nickname)
                    {
                        await using var uow = _db.GetDbContext();
                        var logChannel1 = logChannel;
                        embed.WithAuthor(eab => eab.WithName($"👥 {GetText(logChannel1.Guild, "nick_change")}"))
                             .WithDescription($"**Old Nickname**\n=> {cacheable.Value.Nickname ?? cacheable.Value.Username}\n**New Nickname**\n=> {after.Nickname ?? after.Username}\n**Nickname Chnaged Count**\n=> {uow.Nicknames.GetNicknames(after.Id, cacheable.Value.Guild.Id).Count()}\n**Changed On**\n=> {TimestampTag.FromDateTime(DateTime.UtcNow)}");

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
                            await Task.Delay(1000).ConfigureAwait(false);
                            var diffRoles = cacheable.Value.Roles
                                .Where(r => !after.Roles.Contains(r) && !IsRoleDeleted(r.Id))
                                .Select(r => r.Name)
                                .ToList();

                            if (diffRoles.Count > 0)
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
        }, TaskCreationOptions.LongRunning);
        return Task.CompletedTask;
    }

    private Task Client_ChannelUpdated(IChannel cbefore, IChannel cafter)
    {
        _ = Task.Factory.StartNew(async () =>
        {
            try
            {
                if (cbefore is not IGuildChannel before)
                    return;

                var after = (IGuildChannel)cafter;

                if (!GuildLogSettings.TryGetValue(before.Guild.Id, out var logSetting)
                    || logSetting.ChannelUpdatedId == null
                    || logSetting.IgnoredChannels.Any(ilc => ilc.ChannelId == after.Id))
                {
                    return;
                }

                ITextChannel logChannel;
                if ((logChannel = await TryGetLogChannel(before.Guild, logSetting, LogType.ChannelUpdated)
                        .ConfigureAwait(false)) == null)
                {
                    return;
                }

                var embed = new EmbedBuilder().WithOkColor()
                    .WithFooter(efb => efb.WithText(CurrentTime(before.Guild)));

                var beforeTextChannel = cbefore as ITextChannel;
                var afterTextChannel = cafter as ITextChannel;

                if (before.Name != after.Name)
                {
                    embed.WithTitle($"ℹ️ {GetText(logChannel.Guild, "ch_name_change")}")
                                        .WithDescription($"{after} | {after.Id}")
                                        .AddField(efb =>
                                            efb.WithName(GetText(logChannel.Guild, "ch_old_name")).WithValue(before.Name));
                }
                else if (beforeTextChannel?.Topic != afterTextChannel?.Topic)
                {
                    embed.WithTitle($"ℹ️ {GetText(logChannel.Guild, "ch_topic_change")}")
                                        .WithDescription($"{after} | {after.Id}")
                                        .AddField(efb =>
                                            efb.WithName(GetText(logChannel.Guild, "old_topic"))
                                                .WithValue(beforeTextChannel?.Topic ?? "-"))
                                        .AddField(efb =>
                                            efb.WithName(GetText(logChannel.Guild, "new_topic"))
                                                .WithValue(afterTextChannel?.Topic ?? "-"));
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
        }, TaskCreationOptions.LongRunning);
        return Task.CompletedTask;
    }

    private Task Client_ChannelDestroyed(IChannel ich)
    {
        _ = Task.Factory.StartNew(async () =>
        {
            try
            {
                if (ich is not IGuildChannel ch)
                    return;

                if (!GuildLogSettings.TryGetValue(ch.Guild.Id, out var logSetting)
                    || logSetting.ChannelDestroyedId == null
                    || logSetting.IgnoredChannels.Any(ilc => ilc.ChannelId == ch.Id))
                {
                    return;
                }

                ITextChannel logChannel;
                if ((logChannel = await TryGetLogChannel(ch.Guild, logSetting, LogType.ChannelDestroyed)
                        .ConfigureAwait(false)) == null)
                {
                    return;
                }

                var title = GetText(logChannel.Guild, ch is IVoiceChannel ? "voice_chan_destroyed" : "text_chan_destroyed");

                var audits = await ch.Guild.GetAuditLogsAsync().ConfigureAwait(false);
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
        }, TaskCreationOptions.LongRunning);
        return Task.CompletedTask;
    }

    private Task Client_ChannelCreated(IChannel ich)
    {
        _ = Task.Factory.StartNew(async () =>
        {
            try
            {
                if (ich is not IGuildChannel ch)
                    return;

                if (!GuildLogSettings.TryGetValue(ch.Guild.Id, out var logSetting)
                    || logSetting.ChannelCreatedId == null)
                {
                    return;
                }

                ITextChannel logChannel;
                if ((logChannel = await TryGetLogChannel(ch.Guild, logSetting, LogType.ChannelCreated)
                        .ConfigureAwait(false)) == null)
                {
                    return;
                }

                var title = GetText(logChannel.Guild, ch is IVoiceChannel ? "voice_chan_created" : "text_chan_created");

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
        }, TaskCreationOptions.LongRunning);
        return Task.CompletedTask;
    }

    private Task Client_UserVoiceStateUpdated(SocketUser iusr, SocketVoiceState before, SocketVoiceState after)
    {
        _ = Task.Factory.StartNew(async () =>
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
                    || logSetting.LogVoicePresenceId == null)
                {
                    return;
                }

                ITextChannel logChannel;
                if ((logChannel = await TryGetLogChannel(usr.Guild, logSetting, LogType.VoicePresence)
                        .ConfigureAwait(false)) == null)
                {
                    return;
                }

                var str = "";
                if (beforeVch?.Guild == afterVch?.Guild)
                    str = GetText(logChannel.Guild, "log_vc_moved", usr.Username, beforeVch?.Name, afterVch?.Name);
                else if (beforeVch == null)
                    str = GetText(logChannel.Guild, "log_vc_joined", usr.Username, afterVch.Name);
                else if (afterVch == null)
                    str = GetText(logChannel.Guild, "log_vc_left", usr.Username, beforeVch.Name);

                await logChannel.SendConfirmAsync(str).ConfigureAwait(false);
            }
            catch
            {
                // ignored
            }
        }, TaskCreationOptions.LongRunning);
        return Task.CompletedTask;
    }

    private Task Client_UserLeft(SocketGuild guild, SocketUser user)
    {
        _ = Task.Factory.StartNew(async () =>
        {
            try
            {
                if (user is not SocketGuildUser usr) return;
                if (!GuildLogSettings.TryGetValue(guild.Id, out var logSetting)
                    || logSetting.UserLeftId == null)
                {
                    return;
                }

                ITextChannel logChannel;
                if ((logChannel = await TryGetLogChannel(guild, logSetting, LogType.UserLeft)
                        .ConfigureAwait(false)) == null)
                {
                    return;
                }

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
        }, TaskCreationOptions.LongRunning);
        return Task.CompletedTask;
    }

    private Task Client_UserJoined(IGuildUser usr)
    {
        _ = Task.Factory.StartNew(async () =>
        {
            try
            {
                if (!GuildLogSettings.TryGetValue(usr.Guild.Id, out var logSetting)
                    || logSetting.UserJoinedId == null)
                {
                    return;
                }

                ITextChannel logChannel;
                if ((logChannel = await TryGetLogChannel(usr.Guild, logSetting, LogType.UserJoined)
                        .ConfigureAwait(false)) == null)
                {
                    return;
                }

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
        }, TaskCreationOptions.LongRunning);
        return Task.CompletedTask;
    }

    private Task Client_UserUnbanned(IUser usr, IGuild guild)
    {
        _ = Task.Factory.StartNew(async () =>
        {
            try
            {
                if (!GuildLogSettings.TryGetValue(guild.Id, out var logSetting)
                    || logSetting.UserUnbannedId == null)
                {
                    return;
                }

                ITextChannel logChannel;
                if ((logChannel = await TryGetLogChannel(guild, logSetting, LogType.UserUnbanned)
                        .ConfigureAwait(false)) == null)
                {
                    return;
                }

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
        }, TaskCreationOptions.LongRunning);
        return Task.CompletedTask;
    }

    private Task Client_UserBanned(IUser usr, IGuild guild)
    {
        _ = Task.Factory.StartNew(async () =>
        {
            try
            {
                if (!GuildLogSettings.TryGetValue(guild.Id, out var logSetting)
                    || logSetting.UserBannedId == null)
                {
                    return;
                }

                var bannedby = (await guild.GetAuditLogsAsync(actionType: ActionType.Ban)).FirstOrDefault();
                ITextChannel logChannel;
                if ((logChannel =
                        await TryGetLogChannel(guild, logSetting, LogType.UserBanned).ConfigureAwait(false)) ==
                    null)
                {
                    return;
                }

                var embed = new EmbedBuilder()
                    .WithOkColor()
                    .WithTitle($"🚫 {GetText(logChannel.Guild, "user_banned")}")
                    .WithDescription(usr.ToString());

                if (bannedby != null)
                {
                    embed
                    .AddField("Banned by", bannedby.User)
                    .AddField("Reason", bannedby.Reason ?? "None" );
                }

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
        }, TaskCreationOptions.LongRunning);
        return Task.CompletedTask;
    }

    private Task Client_BulkDelete(IReadOnlyCollection<Cacheable<IMessage, ulong>> messages,
        Cacheable<IMessageChannel, ulong> channel)
    {
        _ = Task.Factory.StartNew(async () =>
        {
            if (channel.Value is not ITextChannel chan)
                return;

            if (!GuildLogSettings.TryGetValue(chan.Guild.Id, out var logSetting)
                || logSetting.MessageDeletedId == null
                || logSetting.IgnoredChannels.Any(ilc => ilc.ChannelId == channel.Id))
            {
                return;
            }

            ITextChannel logChannel;
            if ((logChannel = await TryGetLogChannel(chan.Guild, logSetting, LogType.MessageDeleted)
                    .ConfigureAwait(false)) == null)
            {
                return;
            }

            var toSend = new List<IUserMessage>();
            foreach (var message in messages)
            {
                if ((message.HasValue ? message.Value : null) is IUserMessage msg && !msg.IsAuthor(_client) &&
                    !_ignoreMessageIds.Contains(msg.Id))
                {
                    toSend.Add(msg);
                }
            }

            var count = toSend.Count;

            if (count == 1)
                return;

            while (toSend.Count > 0)
            {
                var toBatch = toSend.Take(100);
                foreach (var group in toBatch.Chunk(20))
                {
                    var eb = new EmbedBuilder().WithOkColor();
                    eb.WithTitle($"🗑 {count} Messages bulk deleted in {channel.Value.Name}");
                    eb.WithDescription(string.Join("\n",
                        group.Select(x => $"{x.Author}: {x.Content}".Truncate(202))));
                    await logChannel.SendMessageAsync(embed: eb.Build()).ConfigureAwait(false);
                }

                toSend = toSend.Skip(100).ToList();
                await Task.Delay(1000).ConfigureAwait(false);
            }
        }, TaskCreationOptions.LongRunning);

        return Task.CompletedTask;
    }

    private Task Client_MessageDeleted(Cacheable<IMessage, ulong> optMsg, Cacheable<IMessageChannel, ulong> ch)
    {
        _ = Task.Factory.StartNew(async () =>
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
                {
                    return;
                }

                ITextChannel logChannel;
                if ((logChannel = await TryGetLogChannel(channel.Guild, logSetting, LogType.MessageDeleted)
                        .ConfigureAwait(false)) == null || logChannel.Id == msg.Id)
                {
                    return;
                }

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
                if (msg.Attachments.Count > 0)
                {
                    embed.AddField(efb =>
                        efb.WithName(GetText(logChannel.Guild, "attachments"))
                            .WithValue(string.Join(", ", msg.Attachments.Select(a => a.Url))).WithIsInline(false));
                }

                await logChannel.EmbedAsync(embed).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // ignored
            }
        }, TaskCreationOptions.LongRunning);
        return Task.CompletedTask;
    }

    private Task Client_MessageUpdated(Cacheable<IMessage, ulong> optmsg, SocketMessage imsg2,
        ISocketMessageChannel ch)
    {
        _ = Task.Factory.StartNew(async () =>
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
                {
                    return;
                }

                ITextChannel logChannel;
                if ((logChannel = await TryGetLogChannel(channel.Guild, logSetting, LogType.MessageUpdated)
                        .ConfigureAwait(false)) == null || logChannel.Id == after.Channel.Id)
                {
                    return;
                }

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
        }, TaskCreationOptions.LongRunning);
        return Task.CompletedTask;
    }

    private async Task<ITextChannel> TryGetLogChannel(IGuild guild, LogSetting logSetting, LogType logChannelType)
    {
        ulong? id = logChannelType switch
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
            LogType.EventCreated => logSetting.EventCreatedId,
            LogType.NicknameUpdated => logSetting.NicknameUpdatedId,
            LogType.RoleCreated => logSetting.RoleCreatedId,
            LogType.RoleUpdated => logSetting.RoleUpdatedId,
            LogType.ServerUpdated => logSetting.ServerUpdatedId,
            LogType.ThreadCreated => logSetting.ThreadCreatedId,
            LogType.ThreadDeleted => logSetting.ThreadDeletedId,
            LogType.ThreadUpdated => logSetting.ThreadUpdatedId,
            LogType.UsernameUpdated => logSetting.UsernameUpdatedId,
            LogType.UserRoleAdded => logSetting.UserRoleAddedId,
            LogType.UserRoleRemoved => logSetting.UserRoleRemovedId,
            _ => 0
        };

        if (id is 0 or null)
        {
            await SetLogChannel(guild.Id, id.GetValueOrDefault(), logChannelType);
            return null;
        }

        var channel = await guild.GetTextChannelAsync(id.GetValueOrDefault()).ConfigureAwait(false);

        return channel;
    }
}