﻿using Discord.Net;
using Serilog;
using System.Net;
using System.Threading.Tasks;

namespace Mewdeko.Modules.Administration.Services;

public sealed class AutoAssignRoleService : INService
{
    private readonly Channel<SocketGuildUser> _assignQueue = Channel.CreateBounded<SocketGuildUser>(
        new BoundedChannelOptions(int.MaxValue)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

    //guildid/roleid
    private readonly DbService _db;
    private readonly GuildSettingsService _guildSettings;

    public AutoAssignRoleService(DiscordSocketClient client, DbService db,
        GuildSettingsService guildSettings)
    {
        _db = db;
        _guildSettings = guildSettings;
        _ = Task.Factory.StartNew(async () =>
        {
            while (true)
            {
                var user = await _assignQueue.Reader.ReadAsync();
                TryGetNormalRoles(user.Guild.Id, out var autoroles);
                TryGetBotRoles(user.Guild.Id, out var autobotroles);
                if (user.IsBot && autobotroles.Count > 0)
                {
                    try
                    {
                        var roleIds = autobotroles
                                      .Select(roleId => user.Guild.GetRole(roleId))
                                      .Where(x => x is not null)
                                      .ToList();

                        if (roleIds.Count > 0)
                        {
                            try
                            {
                                await user.AddRolesAsync(roleIds).ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine(ex);
                            }

                            continue;
                        }

                        Log.Warning(
                            "Disabled 'Auto assign bot role' feature on {GuildName} [{GuildId}] server the roles dont exist",
                            user.Guild.Name,
                            user.Guild.Id);

                        await DisableAabrAsync(user.Guild.Id).ConfigureAwait(false);
                        continue;
                    }
                    catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.Forbidden)
                    {
                        Log.Warning(
                            "Disabled 'Auto assign bot role' feature on {GuildName} [{GuildId}] server because I don't have role management permissions",
                            user.Guild.Name,
                            user.Guild.Id);

                        await DisableAabrAsync(user.Guild.Id).ConfigureAwait(false);
                        continue;
                    }
                    catch
                    {
                        Log.Warning("Error in aar. Probably one of the roles doesn't exist");
                        continue;
                    }
                }

                if (autoroles.Count == 0) continue;
                {
                    try
                    {
                        var roleIds = autoroles
                                      .Select(roleId => user.Guild.GetRole(roleId))
                                      .Where(x => x is not null)
                                      .ToList();

                        if (roleIds.Count > 0)
                        {
                            await user.AddRolesAsync(roleIds).ConfigureAwait(false);
                            await Task.Delay(250).ConfigureAwait(false);
                            continue;
                        }

                        Log.Warning(
                            "Disabled 'Auto assign  role' feature on {GuildName} [{GuildId}] server the roles dont exist",
                            user.Guild.Name,
                            user.Guild.Id);

                        await DisableAarAsync(user.Guild.Id).ConfigureAwait(false);
                    }
                    catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.Forbidden)
                    {
                        Log.Warning(
                            "Disabled 'Auto assign bot role' feature on {GuildName} [{GuildId}] server because I don't have role management permissions",
                            user.Guild.Name,
                            user.Guild.Id);

                        await DisableAarAsync(user.Guild.Id).ConfigureAwait(false);
                    }
                    catch
                    {
                        Log.Warning("Error in aar. Probably one of the roles doesn't exist");
                    }
                }
            }
        }, TaskCreationOptions.LongRunning);

        client.UserJoined += OnClientOnUserJoined;
        client.RoleDeleted += OnClientRoleDeleted;
    }

    private async Task OnClientRoleDeleted(SocketRole role)
    {
        var broles = _guildSettings.GetGuildConfig(role.Guild.Id).AutoBotRoleIds;
        var roles = _guildSettings.GetGuildConfig(role.Guild.Id).AutoAssignRoleId;
        if (!string.IsNullOrWhiteSpace(roles)
            && roles.Split(" ").Select(ulong.Parse).Contains(role.Id))
        {
            await ToggleAarAsync(role.Guild.Id, role.Id).ConfigureAwait(false);
        }

        if (!string.IsNullOrWhiteSpace(broles)
            && broles.Split(" ").Select(ulong.Parse).Contains(role.Id))
        {
            await ToggleAabrAsync(role.Guild.Id, role.Id).ConfigureAwait(false);
        }
    }

    private async Task OnClientOnUserJoined(SocketGuildUser user)
    {
        TryGetBotRoles(user.Guild.Id, out var broles);
        TryGetNormalRoles(user.Guild.Id, out var roles);
        if (user.IsBot && broles.Count > 0)
            await _assignQueue.Writer.WriteAsync(user).ConfigureAwait(false);
        if (roles.Count > 0)
            await _assignQueue.Writer.WriteAsync(user).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ulong>> ToggleAarAsync(ulong guildId, ulong roleId)
    {
        await using var uow = _db.GetDbContext();
        var gc = uow.ForGuildId(guildId, set => set);
        var roles = gc.GetAutoAssignableRoles();
        if (!roles.Remove(roleId) && roles.Count < 10)
            roles.Add(roleId);

        gc.SetAutoAssignableRoles(roles);
        await uow.SaveChangesAsync().ConfigureAwait(false);
        _guildSettings.UpdateGuildConfig(guildId, gc);

        return roles;
    }

    private async Task DisableAarAsync(ulong guildId)
    {
        await using var uow = _db.GetDbContext();
        var gc = uow.ForGuildId(guildId, set => set);
        gc.AutoAssignRoleId = "";
        _guildSettings.UpdateGuildConfig(guildId, gc);
        await uow.SaveChangesAsync().ConfigureAwait(false);
    }

    public async Task SetAabrRolesAsync(ulong guildId, IEnumerable<ulong> newRoles)
    {
        await using var uow = _db.GetDbContext();

        var gc = uow.ForGuildId(guildId, set => set);
        gc.SetAutoAssignableBotRoles(newRoles);
        _guildSettings.UpdateGuildConfig(guildId, gc);
        await uow.SaveChangesAsync().ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ulong>> ToggleAabrAsync(ulong guildId, ulong roleId)
    {
        await using var uow = _db.GetDbContext();
        var gc = uow.ForGuildId(guildId, set => set);
        var roles = gc.GetAutoAssignableBotRoles();
        if (!roles.Remove(roleId) && roles.Count < 10)
            roles.Add(roleId);

        gc.SetAutoAssignableBotRoles(roles);
        await uow.SaveChangesAsync().ConfigureAwait(false);
        _guildSettings.UpdateGuildConfig(guildId, gc);

        return roles;
    }

    public async Task DisableAabrAsync(ulong guildId)
    {
        await using var uow = _db.GetDbContext();
        var gc = uow.ForGuildId(guildId, set => set);
        gc.AutoBotRoleIds = " ";
        _guildSettings.UpdateGuildConfig(guildId, gc);
        await uow.SaveChangesAsync().ConfigureAwait(false);
    }

    public async Task SetAarRolesAsync(ulong guildId, IEnumerable<ulong> newRoles)
    {
        await using var uow = _db.GetDbContext();
        var gc = uow.ForGuildId(guildId, set => set);
        gc.SetAutoAssignableRoles(newRoles);
        _guildSettings.UpdateGuildConfig(guildId, gc);
        await uow.SaveChangesAsync().ConfigureAwait(false);
    }

    public IEnumerable<ulong> TryGetNormalRoles(ulong guildId, out List<ulong> roles)
    {
        var tocheck = _guildSettings.GetGuildConfig(guildId).AutoAssignRoleId;
        if (string.IsNullOrWhiteSpace(tocheck) || tocheck == null)
            roles = new List<ulong>();
        else
            roles = tocheck.Split(" ").Select(ulong.Parse).ToList();
        return roles;
    }

    public IEnumerable<ulong> TryGetBotRoles(ulong guildId, out List<ulong> roles)
    {
        var tocheck = _guildSettings.GetGuildConfig(guildId).AutoBotRoleIds;
        if (string.IsNullOrWhiteSpace(tocheck) || tocheck == null)
            roles = new List<ulong>();
        else
            roles = tocheck.Split(" ").Select(ulong.Parse).ToList();
        return roles;
    }
}

public static class GuildConfigExtensions
{
    public static List<ulong> GetAutoAssignableRoles(this GuildConfig gc)
        => string.IsNullOrWhiteSpace(gc.AutoAssignRoleId) ? new List<ulong>() : gc.AutoAssignRoleId.Split(" ").Select(ulong.Parse).ToList();

    public static void SetAutoAssignableRoles(this GuildConfig gc, IEnumerable<ulong> roles) => gc.AutoAssignRoleId = roles.JoinWith(" ");

    public static List<ulong> GetAutoAssignableBotRoles(this GuildConfig gc)
        => string.IsNullOrWhiteSpace(gc.AutoBotRoleIds) ? new List<ulong>() : gc.AutoBotRoleIds.Split(" ").Select(ulong.Parse).ToList();

    public static void SetAutoAssignableBotRoles(this GuildConfig gc, IEnumerable<ulong> roles) => gc.AutoBotRoleIds = roles.JoinWith(" ");
}