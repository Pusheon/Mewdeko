using System.Collections.Concurrent;
using System.Threading;
using Discord;
using Discord.WebSocket;
using Mewdeko._Extensions;
using Mewdeko.Common.Collections;
using Mewdeko.Database;
using Mewdeko.Database.Extensions;
using Mewdeko.Database.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Mewdeko.Modules.Moderation.Services;

public enum MuteType
{
    Voice,
    Chat,
    All
}

public class MuteService : INService
{
    public enum TimerType
    {
        Mute,
        Ban,
        AddRole
    }

    private static readonly OverwritePermissions _denyOverwrite =
        new(addReactions: PermValue.Deny, sendMessages: PermValue.Deny,
            attachFiles: PermValue.Deny, sendMessagesInThreads: PermValue.Deny, createPublicThreads: PermValue.Deny);

    private readonly DiscordSocketClient _client;
    private readonly DbService _db;
    public string[] Uroles;

    public MuteService(DiscordSocketClient client, DbService db, Mewdeko bot)
    {
        _client = client;
        _db = db;
        Removerolesonmute = bot.AllGuildConfigs
            .ToDictionary(x => x.GuildId, x => x.removeroles)
            .ToConcurrent();
        using (var uow = db.GetDbContext())
        {
            var guildIds = client.Guilds.Select(x => x.Id).ToList();
            var configs = uow.GuildConfigs.AsQueryable()
                .Include(x => x.MutedUsers)
                .Include(x => x.UnbanTimer)
                .Include(x => x.UnmuteTimers)
                .Include(x => x.UnroleTimer)
                .Where(x => guildIds.Contains(x.GuildId))
                .ToList();

            GuildMuteRoles = configs
                .Where(c => !string.IsNullOrWhiteSpace(c.MuteRoleName))
                .ToDictionary(c => c.GuildId, c => c.MuteRoleName)
                .ToConcurrent();

            MutedUsers = new ConcurrentDictionary<ulong, ConcurrentHashSet<ulong>>(configs
                .ToDictionary(
                    k => k.GuildId,
                    v => new ConcurrentHashSet<ulong>(v.MutedUsers.Select(m => m.UserId))
                ));

            var max = TimeSpan.FromDays(49);

            foreach (var conf in configs)
            {
                foreach (var x in conf.UnmuteTimers)
                {
                    TimeSpan after;
                    if (x.UnmuteAt - TimeSpan.FromMinutes(2) <= DateTime.UtcNow)
                    {
                        after = TimeSpan.FromMinutes(2);
                    }
                    else
                    {
                        var unmute = x.UnmuteAt - DateTime.UtcNow;
                        after = unmute > max ? max : unmute;
                    }

                    StartUn_Timer(conf.GuildId, x.UserId, after, TimerType.Mute);
                }

                foreach (var x in conf.UnbanTimer)
                {
                    TimeSpan after;
                    if (x.UnbanAt - TimeSpan.FromMinutes(2) <= DateTime.UtcNow)
                    {
                        after = TimeSpan.FromMinutes(2);
                    }
                    else
                    {
                        var unban = x.UnbanAt - DateTime.UtcNow;
                        after = unban > max ? max : unban;
                    }

                    StartUn_Timer(conf.GuildId, x.UserId, after, TimerType.Ban);
                }

                foreach (var x in conf.UnroleTimer)
                {
                    TimeSpan after;
                    if (x.UnbanAt - TimeSpan.FromMinutes(2) <= DateTime.UtcNow)
                    {
                        after = TimeSpan.FromMinutes(2);
                    }
                    else
                    {
                        var unban = x.UnbanAt - DateTime.UtcNow;
                        after = unban > max ? max : unban;
                    }

                    StartUn_Timer(conf.GuildId, x.UserId, after, TimerType.AddRole, x.RoleId);
                }
            }

            _client.UserJoined += Client_UserJoined;
        }

        UserMuted += OnUserMuted;
        UserUnmuted += OnUserUnmuted;
    }

    public ConcurrentDictionary<ulong, string> GuildMuteRoles { get; }
    public ConcurrentDictionary<ulong, ConcurrentHashSet<ulong>> MutedUsers { get; }

    public ConcurrentDictionary<ulong, ConcurrentDictionary<(ulong, TimerType), Timer>> UnTimers { get; }
        = new();

    private ConcurrentDictionary<ulong, int> Removerolesonmute { get; }

    public event Action<IGuildUser, IUser, MuteType, string> UserMuted = delegate { };
    public event Action<IGuildUser, IUser, MuteType, string> UserUnmuted = delegate { };
    

    private void OnUserMuted(IGuildUser user, IUser mod, MuteType type, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return;

        var _ = Task.Run(() => user.SendMessageAsync(embed: new EmbedBuilder()
            .WithDescription($"You've been muted in {user.Guild} server")
            .AddField("Mute Type", type.ToString())
            .AddField("Moderator", mod.ToString())
            .AddField("Reason", reason)
            .Build()));
    }

    private void OnUserUnmuted(IGuildUser user, IUser mod, MuteType type, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return;

        var _ = Task.Run(() => user.SendMessageAsync(embed: new EmbedBuilder()
            .WithDescription($"You've been unmuted in {user.Guild} server")
            .AddField("Unmute Type", type.ToString())
            .AddField("Moderator", mod.ToString())
            .AddField("Reason", reason)
            .Build()));
    }

    private Task Client_UserJoined(IGuildUser usr)
    {
        try
        {
            MutedUsers.TryGetValue(usr.Guild.Id, out var muted);

            if (muted == null || !muted.Contains(usr.Id))
                return Task.CompletedTask;
            var _ = Task.Run(() => MuteUser(usr, _client.CurrentUser, reason: "Sticky mute").ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error in MuteService UserJoined event");
        }

        return Task.CompletedTask;
    }

    public async Task SetMuteRoleAsync(ulong guildId, string name)
    {
        await using var uow = _db.GetDbContext();
        var config = uow.ForGuildId(guildId, set => set);
        config.MuteRoleName = name;
        GuildMuteRoles.AddOrUpdate(guildId, name, (_, _) => name);
        await uow.SaveChangesAsync();
    }

    public async Task MuteUser(IGuildUser usr, IUser mod, MuteType type = MuteType.All, string reason = "")
    {
        switch (type)
        {
            case MuteType.All:
            {
                try
                {
                    await usr.ModifyAsync(x => x.Mute = true).ConfigureAwait(false);
                }
                catch
                {
                    // ignored
                }

                await using var uow = _db.GetDbContext();
                var config = uow.ForGuildId(usr.Guild.Id,
                    set => set.Include(gc => gc.MutedUsers).Include(gc => gc.UnmuteTimers));
                var roles = usr.GetRoles().Where(p => p.Tags == null).Except(new[] {usr.Guild.EveryoneRole});
                var enumerable = roles as IRole[] ?? roles.ToArray();
                var uroles = string.Join(" ", enumerable.Select(x => x.Id));
                if (GetRemoveOnMute(usr.Guild.Id) == 0) config.MutedUsers.Add(new MutedUserId {UserId = usr.Id});
                if (GetRemoveOnMute(usr.Guild.Id) == 1)
                    config.MutedUsers.Add(new MutedUserId {UserId = usr.Id, roles = uroles});
                if (MutedUsers.TryGetValue(usr.Guild.Id, out var muted)) muted.Add(usr.Id);

                config.UnmuteTimers.RemoveWhere(x => x.UserId == usr.Id);

                await uow.SaveChangesAsync();
                var muteRole = await GetMuteRole(usr.Guild).ConfigureAwait(false);
                if (!usr.RoleIds.Contains(muteRole.Id))
                    if (GetRemoveOnMute(usr.Guild.Id) == 1)
                        await usr.RemoveRolesAsync(enumerable);
                await usr.AddRoleAsync(muteRole).ConfigureAwait(false);
                StopTimer(usr.GuildId, usr.Id, TimerType.Mute);

                UserMuted(usr, mod, MuteType.All, reason);
                break;
            }
            case MuteType.Voice:
                try
                {
                    await usr.ModifyAsync(x => x.Mute = true).ConfigureAwait(false);
                    UserMuted(usr, mod, MuteType.Voice, reason);
                }
                catch
                {
                    // ignored
                }

                break;
            case MuteType.Chat:
                await usr.AddRoleAsync(await GetMuteRole(usr.Guild).ConfigureAwait(false)).ConfigureAwait(false);
                UserMuted(usr, mod, MuteType.Chat, reason);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, null);
        }
    }

    public int GetRemoveOnMute(ulong? id)
    {
        if (id == null || !Removerolesonmute.TryGetValue(id.Value, out var removeroles))
            return 0;

        return removeroles;
    }

    public async Task Removeonmute(IGuild guild, string yesnt)
    {
        var yesno = -1;
        await using (var uow = _db.GetDbContext())
        {
            yesno = yesnt switch
            {
                "y" => 1,
                "n" => 0,
                _ => yesno
            };

            var gc = uow.ForGuildId(guild.Id, set => set);
            gc.removeroles = yesno;
            await uow.SaveChangesAsync();
        }

        Removerolesonmute.AddOrUpdate(guild.Id, yesno, (_, _) => yesno);
    }

    public async Task UnmuteUser(ulong guildId, ulong usrId, IUser mod, MuteType type = MuteType.All,
        string reason = "")
    {
        var usr = _client.GetGuild(guildId)?.GetUser(usrId);
        switch (type)
        {
            case MuteType.All:
                {
                    StopTimer(guildId, usrId, TimerType.Mute);
                    await using (var uow = _db.GetDbContext())
                    {
                        var config = uow.ForGuildId(guildId, set => set.Include(gc => gc.MutedUsers)
                                                                       .Include(gc => gc.UnmuteTimers));
                        if (usr != null && GetRemoveOnMute(usr.Guild.Id) == 1)
                        {
                            try
                            {
                                Uroles = config.MutedUsers
                                               .FirstOrDefault(p => p.UserId == usr.Id && p.roles != null)
                                               ?.roles
                                               .Split(' ');
                            }
                            catch (Exception)
                            {
                                // ignored
                            }

                            if (Uroles != null)
                                foreach (var i in Uroles)
                                    if (ulong.TryParse(i, out var roleId))
                                        try
                                        {
                                            await usr.AddRoleAsync(usr.Guild.GetRole(roleId));
                                        }
                                        catch
                                        {
                                            // ignored
                                        }
                        }

                        var match = new MutedUserId
                        {
                            UserId = usrId
                        };
                        var toRemove = config.MutedUsers.FirstOrDefault(x => x.Equals(match));

                        if (toRemove != null) uow.Remove(toRemove);
                        if (MutedUsers.TryGetValue(guildId, out var muted))
                            muted.TryRemove(usrId);

                        config.UnmuteTimers.RemoveWhere(x => x.UserId == usrId);

                        await uow.SaveChangesAsync();
                    }

                    if (usr != null)
                    {
                        try
                        {
                            await usr.ModifyAsync(x => x.Mute = false).ConfigureAwait(false);
                        }
                        catch
                        {
                            // ignored
                        }

                        try
                        {
                            await usr.RemoveRoleAsync(await GetMuteRole(usr.Guild).ConfigureAwait(false))
                                     .ConfigureAwait(false);
                        }
                        catch
                        {
                            /*ignore*/
                        }

                        UserUnmuted(usr, mod, MuteType.All, reason);
                    }

                    break;
                }
            case MuteType.Voice when usr == null:
                return;
            case MuteType.Voice:
                try
                {
                    await usr.ModifyAsync(x => x.Mute = false).ConfigureAwait(false);
                    UserUnmuted(usr, mod, MuteType.Voice, reason);
                }
                catch
                {
                    // ignored
                }

                break;
            case MuteType.Chat when usr == null:
                return;
            case MuteType.Chat:
                await usr.RemoveRoleAsync(await GetMuteRole(usr.Guild).ConfigureAwait(false)).ConfigureAwait(false);
                UserUnmuted(usr, mod, MuteType.Chat, reason);
                break;
        }
    }

    public async Task<IRole> GetMuteRole(IGuild guild)
    {
        if (guild == null)
            throw new ArgumentNullException(nameof(guild));

        const string defaultMuteRoleName = "Mewdeko-mute";

        var muteRoleName = GuildMuteRoles.GetOrAdd(guild.Id, defaultMuteRoleName);

        var muteRole = guild.Roles.FirstOrDefault(r => r.Name == muteRoleName);
        if (muteRole == null)
            //if it doesn't exist, create it
            try
            {
                muteRole = await guild.CreateRoleAsync(muteRoleName, isMentionable: false).ConfigureAwait(false);
            }
            catch
            {
                //if creations fails,  maybe the name != correct, find default one, if doesn't work, create default one
                muteRole = guild.Roles.FirstOrDefault(r => r.Name == muteRoleName) ??
                           await guild.CreateRoleAsync(defaultMuteRoleName, isMentionable: false)
                               .ConfigureAwait(false);
            }

        foreach (var toOverwrite in await guild.GetTextChannelsAsync().ConfigureAwait(false))
            try
            {
                if (toOverwrite is IThreadChannel)
                    continue;
                if (toOverwrite.PermissionOverwrites.Any(x => x.TargetId == muteRole.Id
                                                              && x.TargetType == PermissionTarget.Role)) continue;
                await toOverwrite.AddPermissionOverwriteAsync(muteRole, _denyOverwrite)
                                 .ConfigureAwait(false);

                await Task.Delay(200).ConfigureAwait(false);
            }
            catch
            {
                // ignored
            }

        return muteRole;
    }

    public async Task TimedMute(IGuildUser user, IUser mod, TimeSpan after, MuteType muteType = MuteType.All,
        string reason = "")
    {
        await MuteUser(user, mod, muteType, reason)
            .ConfigureAwait(false); // mute the user. This will also remove any previous unmute timers
        await using (var uow = _db.GetDbContext())
        {
            var config = uow.ForGuildId(user.GuildId, set => set.Include(x => x.UnmuteTimers));
            config.UnmuteTimers.Add(new UnmuteTimer
            {
                UserId = user.Id,
                UnmuteAt = DateTime.UtcNow + after
            }); // add teh unmute timer to the database
            await uow.SaveChangesAsync();
        }

        StartUn_Timer(user.GuildId, user.Id, after, TimerType.Mute); // start the timer
    }

    public async Task TimedBan(IGuild guild, IUser user, TimeSpan after, string reason)
    {
        await guild.AddBanAsync(user.Id, 0, reason).ConfigureAwait(false);
        await using (var uow = _db.GetDbContext())
        {
            var config = uow.ForGuildId(guild.Id, set => set.Include(x => x.UnbanTimer));
            config.UnbanTimer.Add(new UnbanTimer
            {
                UserId = user.Id,
                UnbanAt = DateTime.UtcNow + after
            }); // add teh unmute timer to the database
            await uow.SaveChangesAsync();
        }

        StartUn_Timer(guild.Id, user.Id, after, TimerType.Ban); // start the timer
    }

    public async Task TimedRole(IGuildUser user, TimeSpan after, string reason, IRole role)
    {
        await user.AddRoleAsync(role).ConfigureAwait(false);
        await using (var uow = _db.GetDbContext())
        {
            var config = uow.ForGuildId(user.GuildId, set => set.Include(x => x.UnroleTimer));
            config.UnroleTimer.Add(new UnroleTimer
            {
                UserId = user.Id,
                UnbanAt = DateTime.UtcNow + after,
                RoleId = role.Id
            }); // add teh unmute timer to the database
            await uow.SaveChangesAsync();
        }

        StartUn_Timer(user.GuildId, user.Id, after, TimerType.AddRole, role.Id); // start the timer
    }

    public void StartUn_Timer(ulong guildId, ulong userId, TimeSpan after, TimerType type, ulong? roleId = null)
    {
        //load the unmute timers for this guild
        var userUnTimers = UnTimers.GetOrAdd(guildId, new ConcurrentDictionary<(ulong, TimerType), Timer>());

        //unmute timer to be added
        // ReSharper disable once AsyncVoidLambda
        var toAdd = new Timer(async _ =>
        {
            switch (type)
            {
                case TimerType.Ban:
                    try
                    {
                        RemoveTimerFromDb(guildId, userId, type);
                        StopTimer(guildId, userId, type);
                        var guild = _client.GetGuild(guildId); // load the guild
                        if (guild != null) await guild.RemoveBanAsync(userId).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Couldn't unban user {0} in guild {1}", userId, guildId);
                    }

                    break;
                case TimerType.AddRole:
                    try
                    {
                        RemoveTimerFromDb(guildId, userId, type);
                        StopTimer(guildId, userId, type);
                        var guild = _client.GetGuild(guildId);
                        var user = guild?.GetUser(userId);
                        if (guild == null) return;
                        if (roleId == null) return;
                        var role = guild.GetRole(roleId.Value);
                        if (user != null && user.Roles.Contains(role))
                            await user.RemoveRoleAsync(role).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Couldn't remove role from user {0} in guild {1}", userId, guildId);
                    }

                    break;
                default:
                    try
                    {
                        // unmute the user, this will also remove the timer from the db
                        await UnmuteUser(guildId, userId, _client.CurrentUser, reason: "Timed mute expired")
                            .ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        RemoveTimerFromDb(guildId, userId, type); // if unmute errored, just remove unmute from db
                        Log.Warning(ex, "Couldn't unmute user {0} in guild {1}", userId, guildId);
                    }

                    break;
            }
        }, null, after, Timeout.InfiniteTimeSpan);

        //add it, or stop the old one and add this one
        userUnTimers.AddOrUpdate((userId, type), _ => toAdd, (_, old) =>
        {
            old.Change(Timeout.Infinite, Timeout.Infinite);
            return toAdd;
        });
    }

    public void StopTimer(ulong guildId, ulong userId, TimerType type)
    {
        if (!UnTimers.TryGetValue(guildId, out var userTimer))
            return;

        if (userTimer.TryRemove((userId, type), out var removed))
            removed.Change(Timeout.Infinite, Timeout.Infinite);
    }

    private void RemoveTimerFromDb(ulong guildId, ulong userId, TimerType type)
    {
        using var uow = _db.GetDbContext();
        object toDelete;
        if (type == TimerType.Mute)
        {
            var config = uow.ForGuildId(guildId, set => set.Include(x => x.UnmuteTimers));
            toDelete = config.UnmuteTimers.FirstOrDefault(x => x.UserId == userId);
        }
        else
        {
            var config = uow.ForGuildId(guildId, set => set.Include(x => x.UnbanTimer));
            toDelete = config.UnbanTimer.FirstOrDefault(x => x.UserId == userId);
        }

        if (toDelete != null) uow.Remove(toDelete);
        uow.SaveChanges();
    }
}