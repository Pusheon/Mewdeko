using System.Collections.Concurrent;
using System.Threading;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Mewdeko._Extensions;
using Mewdeko.Common;
using Mewdeko.Common.Replacements;
using Mewdeko.Common.TypeReaders.Models;
using Mewdeko.Database;
using Mewdeko.Database.Extensions;
using Mewdeko.Database.Models;
using Mewdeko.Modules.Permissions.Services;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Serilog;

namespace Mewdeko.Modules.Moderation.Services;

public class UserPunishService : INService
{
    private readonly BlacklistService _blacklistService;
    private readonly DbService _db;
    private readonly MuteService _mute;
    private readonly DiscordSocketClient _client;

    public UserPunishService(MuteService mute, DbService db, BlacklistService blacklistService,
        Mewdeko bot,
        DiscordSocketClient client)
    {
        Warnlogchannelids = bot.AllGuildConfigs
                                .Where(x => x.WarnlogChannelId != 0)
                                .ToDictionary(x => x.GuildId, x => x.WarnlogChannelId)
                                .ToConcurrent();
        _mute = mute;
        _db = db;
        _blacklistService = blacklistService;
        _client = client;
        _ = new Timer(async _ => await CheckAllWarnExpiresAsync(), null,
            TimeSpan.FromSeconds(0), TimeSpan.FromHours(12));
    }

    private ConcurrentDictionary<ulong, ulong> Warnlogchannelids { get; } = new();

    public ulong GetWarnlogChannel(ulong? id)
    {
        if (id == null || !Warnlogchannelids.TryGetValue(id.Value, out var warnlogchannel))
            return 0;

        return warnlogchannel;
    }

    public async Task SetWarnlogChannelId(IGuild guild, ITextChannel channel)
    {
        await using (var uow = _db.GetDbContext())
        {
            var gc = uow.ForGuildId(guild.Id, set => set);
            gc.WarnlogChannelId = channel.Id;
            await uow.SaveChangesAsync();
        }

        Warnlogchannelids.AddOrUpdate(guild.Id, channel.Id, (_, _) => channel.Id);
    }

    public async Task<WarningPunishment> Warn(IGuild guild, ulong userId, IUser mod, string reason)
    {
        var modName = mod.ToString();

        if (string.IsNullOrWhiteSpace(reason))
            reason = "-";

        var guildId = guild.Id;

        var warn = new Warning
        {
            UserId = userId,
            GuildId = guildId,
            Forgiven = false,
            Reason = reason,
            Moderator = modName
        };

        var warnings = 1;
        List<WarningPunishment> ps;
        await using (var uow = _db.GetDbContext())
        {
            ps = uow.ForGuildId(guildId, set => set.Include(x => x.WarnPunishments))
                .WarnPunishments;

            warnings += uow.Warnings
                .ForId(guildId, userId)
                .Count(w => !w.Forgiven && w.UserId == userId);

            uow.Warnings.Add(warn);

            await uow.SaveChangesAsync();
        }

        var p = ps.FirstOrDefault(x => x.Count == warnings);

        if (p != null)
        {
            var user = await guild.GetUserAsync(userId).ConfigureAwait(false);
            if (user == null)
                return null;

            await ApplyPunishment(guild, user, mod, p.Punishment, p.Time, p.RoleId, "Warned too many times.");
            return p;
        }

        return null;
    }

    public async Task ApplyPunishment(IGuild guild, IGuildUser user, IUser mod, PunishmentAction p, int minutes,
        ulong? roleId, string reason)
    {
        switch (p)
        {
            case PunishmentAction.Mute:
                if (minutes == 0)
                    await _mute.MuteUser(user, mod, reason: reason).ConfigureAwait(false);
                else
                    await _mute.TimedMute(user, mod, TimeSpan.FromMinutes(minutes), reason: reason)
                        .ConfigureAwait(false);
                break;
            case PunishmentAction.VoiceMute:
                if (minutes == 0)
                    await _mute.MuteUser(user, mod, MuteType.Voice, reason).ConfigureAwait(false);
                else
                    await _mute.TimedMute(user, mod, TimeSpan.FromMinutes(minutes), MuteType.Voice, reason)
                        .ConfigureAwait(false);
                break;
            case PunishmentAction.ChatMute:
                if (minutes == 0)
                    await _mute.MuteUser(user, mod, MuteType.Chat, reason).ConfigureAwait(false);
                else
                    await _mute.TimedMute(user, mod, TimeSpan.FromMinutes(minutes), MuteType.Chat, reason)
                        .ConfigureAwait(false);
                break;
            case PunishmentAction.Kick:
                await user.KickAsync(reason).ConfigureAwait(false);
                break;
            case PunishmentAction.Ban:
                if (minutes == 0)
                    await guild.AddBanAsync(user, reason: reason).ConfigureAwait(false);
                else
                    await _mute.TimedBan(user.Guild, user, TimeSpan.FromMinutes(minutes), reason)
                        .ConfigureAwait(false);
                break;
            case PunishmentAction.Softban:
                await guild.AddBanAsync(user, 7, $"Softban | {reason}").ConfigureAwait(false);
                try
                {
                    await guild.RemoveBanAsync(user).ConfigureAwait(false);
                }
                catch
                {
                    await guild.RemoveBanAsync(user).ConfigureAwait(false);
                }

                break;
            case PunishmentAction.RemoveRoles:
                await user.RemoveRolesAsync(user.GetRoles().Where(x => !x.IsManaged && x != x.Guild.EveryoneRole))
                    .ConfigureAwait(false);
                break;
            case PunishmentAction.AddRole:
                if (roleId is null)
                    return;
                var role = guild.GetRole(roleId.Value);
                if (role is not null)
                {
                    if (minutes == 0)
                        await user.AddRoleAsync(role).ConfigureAwait(false);
                    else
                        await _mute.TimedRole(user, TimeSpan.FromMinutes(minutes), reason, role)
                            .ConfigureAwait(false);
                }
                else
                {
                    Log.Warning($"Can't find role {roleId.Value} on server {guild.Id} to apply punishment.");
                }
                break;
            case PunishmentAction.Warn:
                await Warn(guild, user.Id, _client.CurrentUser, reason);
                break;
        }
    }

    public async Task CheckAllWarnExpiresAsync()
    {
        await using var uow = _db.GetDbContext();
        var cleared = await uow.Database.ExecuteSqlRawAsync(@"UPDATE Warnings
SET Forgiven = 1,
    ForgivenBy = 'Expiry'
WHERE GuildId in (SELECT GuildId FROM GuildConfigs WHERE WarnExpireHours > 0 AND WarnExpireAction = 0)
	AND Forgiven = 0
	AND DateAdded < datetime('now', (SELECT '-' || WarnExpireHours || ' hours' FROM GuildConfigs as gc WHERE gc.GuildId = Warnings.GuildId));");

        var deleted = await uow.Database.ExecuteSqlRawAsync(@"DELETE FROM Warnings
WHERE GuildId in (SELECT GuildId FROM GuildConfigs WHERE WarnExpireHours > 0 AND WarnExpireAction = 1)
	AND DateAdded < datetime('now', (SELECT '-' || WarnExpireHours || ' hours' FROM GuildConfigs as gc WHERE gc.GuildId = Warnings.GuildId));");

        if (cleared > 0 || deleted > 0)
            Log.Information($"Cleared {cleared} warnings and deleted {deleted} warnings due to expiry.");
    }

    public async Task CheckWarnExpiresAsync(ulong guildId)
    {
        await using var uow = _db.GetDbContext();
        var config = uow.ForGuildId(guildId, inc => inc);

        if (config.WarnExpireHours == 0)
            return;

        var hours = $"{-config.WarnExpireHours} hours";
        switch (config.WarnExpireAction)
        {
            case WarnExpireAction.Clear:
                await uow.Database.ExecuteSqlInterpolatedAsync($@"UPDATE warnings
SET Forgiven = 1,
    ForgivenBy = 'Expiry'
WHERE GuildId={guildId}
    AND Forgiven = 0
    AND DateAdded < datetime('now', {hours})");
                break;
            case WarnExpireAction.Delete:
                await uow.Database.ExecuteSqlInterpolatedAsync($@"DELETE FROM warnings
WHERE GuildId={guildId}
    AND DateAdded < datetime('now', {hours})");
                break;
        }

        await uow.SaveChangesAsync();
    }

    public async Task WarnExpireAsync(ulong guildId, int days, bool delete)
    {
        await using (var uow = _db.GetDbContext())
        {
            var config = uow.ForGuildId(guildId, inc => inc);

            config.WarnExpireHours = days * 24;
            config.WarnExpireAction = delete ? WarnExpireAction.Delete : WarnExpireAction.Clear;
            await uow.SaveChangesAsync();

            // no need to check for warn expires
            if (config.WarnExpireHours == 0)
                return;
        }

        await CheckWarnExpiresAsync(guildId);
    }

    public IGrouping<ulong, Warning>[] WarnlogAll(ulong gid)
    {
        using var uow = _db.GetDbContext();
        return uow.Warnings.GetForGuild(gid).GroupBy(x => x.UserId).ToArray();
    }

    public Warning[] UserWarnings(ulong gid, ulong userId)
    {
        using var uow = _db.GetDbContext();
        return uow.Warnings.ForId(gid, userId);
    }

    public async Task<bool> WarnClearAsync(ulong guildId, ulong userId, int index, string moderator)
    {
        var toReturn = true;
        await using var uow = _db.GetDbContext();
        if (index == 0)
            await uow.Warnings.ForgiveAll(guildId, userId, moderator);
        else
            toReturn = uow.Warnings.Forgive(guildId, userId, moderator, index - 1);
        await uow.SaveChangesAsync();

        return toReturn;
    }

    public bool WarnPunish(ulong guildId, int number, PunishmentAction punish, StoopidTime? time, IRole? role = null)
    {
        // these 3 don't make sense with time
        if (punish is PunishmentAction.Softban or PunishmentAction.Kick or PunishmentAction.RemoveRoles && time != null)
            return false;
        if (number <= 0 || (time != null && time.Time > TimeSpan.FromDays(49)))
            return false;

        using var uow = _db.GetDbContext();
        var ps = uow.ForGuildId(guildId, set => set.Include(x => x.WarnPunishments)).WarnPunishments;
        var toDelete = ps.Where(x => x.Count == number);

        uow.RemoveRange(toDelete);

        ps.Add(new WarningPunishment
        {
            Count = number,
            Punishment = punish,
            Time = (int?) time?.Time.TotalMinutes ?? 0,
            RoleId = punish == PunishmentAction.AddRole ? role.Id : default(ulong?)
        });
        uow.SaveChanges();

        return true;
    }

    public bool WarnPunishRemove(ulong guildId, int number)
    {
        if (number <= 0)
            return false;

        using var uow = _db.GetDbContext();
        var ps = uow.ForGuildId(guildId, set => set.Include(x => x.WarnPunishments)).WarnPunishments;
        var p = ps.FirstOrDefault(x => x.Count == number);

        if (p != null)
        {
            uow.Remove(p);
            uow.SaveChanges();
        }

        return true;
    }

    public WarningPunishment[] WarnPunishList(ulong guildId)
    {
        using var uow = _db.GetDbContext();
        return uow.ForGuildId(guildId, gc => gc.Include(x => x.WarnPunishments))
            .WarnPunishments
            .OrderBy(x => x.Count)
            .ToArray();
    }

    public (IEnumerable<(string Original, ulong? Id, string Reason)> Bans, int Missing) MassKill(SocketGuild guild,
        string people)
    {
        var gusers = guild.Users;
        //get user objects and reasons
        var bans = people.Split("\n")
            .Select(x =>
            {
                var split = x.Trim().Split(" ");

                var reason = string.Join(" ", split.Skip(1));

                if (ulong.TryParse(split[0], out var id))
                    return (Original: split[0], Id: id, Reason: reason);

                return (Original: split[0],
                    gusers
                        .FirstOrDefault(u => u.ToString().ToLowerInvariant() == x)
                        ?.Id,
                    Reason: reason);
            })
            .ToArray();

        //if user is null, means that person couldn't be found
        var missing = bans
            .Count(x => !x.Id.HasValue);

        //get only data for found users
        var found = bans
            .Where(x => x.Id.HasValue)
            .Select(x => x.Id.Value)
            .ToList();

        _blacklistService.BlacklistUsers(found);

        return (bans, missing);
    }

    public string GetBanTemplate(ulong guildId)
    {
        using var uow = _db.GetDbContext();
        var template = uow.BanTemplates
            .AsQueryable()
            .FirstOrDefault(x => x.GuildId == guildId);
        return template?.Text;
    }

    public string GetWarnMessageTemplate(ulong guildId)
    {
        using var uow = _db.GetDbContext();
        return uow.GuildConfigs.AsQueryable().FirstOrDefault(x => x.GuildId == guildId).WarnMessage;
    }

    public void SetBanTemplate(ulong guildId, string? text)
    {
        using var uow = _db.GetDbContext();
        var template = uow.BanTemplates
            .AsQueryable()
            .FirstOrDefault(x => x.GuildId == guildId);

        if (text == null)
        {
            if (template is null)
                return;

            uow.Remove(template);
        }
        else if (template == null)
        {
            uow.BanTemplates.Add(new BanTemplate
            {
                GuildId = guildId,
                Text = text
            });
        }
        else
        {
            template.Text = text;
        }

        uow.SaveChanges();
    }

    public Task<(EmbedBuilder, string)> GetBanUserDmEmbed(ICommandContext context, IGuildUser target, string defaultMessage,
        string banReason, TimeSpan? duration) =>
        GetBanUserDmEmbed(
            (DiscordSocketClient) context.Client,
            (SocketGuild) context.Guild,
            (IGuildUser) context.User,
            target,
            defaultMessage,
            banReason,
            duration);

    public Task<(EmbedBuilder, string)> GetBanUserDmEmbed(DiscordSocketClient client, SocketGuild guild,
        IGuildUser moderator, IGuildUser target, string defaultMessage, string banReason, TimeSpan? duration)
    {
        var template = GetBanTemplate(guild.Id);

        banReason = string.IsNullOrWhiteSpace(banReason)
            ? "-"
            : banReason;

        var replacer = new ReplacementBuilder()
            .WithServer(client, guild)
            .WithOverride("%ban.mod%", moderator.ToString)
            .WithOverride("%ban.mod.fullname%", moderator.ToString)
            .WithOverride("%ban.mod.name%", () => moderator.Username)
            .WithOverride("%ban.mod.discrim%", () => moderator.Discriminator)
            .WithOverride("%ban.user%", target.ToString)
            .WithOverride("%ban.user.fullname%", target.ToString)
            .WithOverride("%ban.user.name%", () => target.Username)
            .WithOverride("%ban.user.discrim%", () => target.Discriminator)
            .WithOverride("%reason%", () => banReason)
            .WithOverride("%ban.reason%", () => banReason)
            .WithOverride("%ban.duration%", () => duration?.ToString(@"d\.hh\:mm") ?? "perma")
            .Build();
        EmbedBuilder embed;
        string plainText;
        // if template isn't set, use the old message style
        if (string.IsNullOrWhiteSpace(template))
        {
            template = JsonConvert.SerializeObject(new
            {
                color = Mewdeko.ErrorColor.RawValue,
                description = defaultMessage
            });

            SmartEmbed.TryParse(replacer.Replace(template), out embed, out plainText);
        }
        // if template is set to "-" do not dm the user
        else if (template == "-")
        {
            return Task.FromResult<(EmbedBuilder, string)>((null, null));
        }
        // otherwise, treat template as a regular string with replacements
        else
        {
            template = JsonConvert.SerializeObject(new
            {
                color = Mewdeko.ErrorColor.RawValue,
                description = replacer.Replace(template)
            });

            SmartEmbed.TryParse(replacer.Replace(template), out embed, out plainText);
        }

        return Task.FromResult((embed, plainText));
    }
}