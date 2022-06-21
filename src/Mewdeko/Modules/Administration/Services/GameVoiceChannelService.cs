﻿using Serilog;
using System.Threading.Tasks;

namespace Mewdeko.Modules.Administration.Services;

public class GameVoiceChannelService : INService
{
    private readonly DbService _db;
    private readonly GuildSettingsService _guildSettings;

    public GameVoiceChannelService(DiscordSocketClient client, DbService db,
        GuildSettingsService guildSettings)
    {
        _db = db;
        _guildSettings = guildSettings;

        client.UserVoiceStateUpdated += Client_UserVoiceStateUpdated;
        client.GuildMemberUpdated += _client_GuildMemberUpdated;
    }

    private Task _client_GuildMemberUpdated(Cacheable<SocketGuildUser, ulong> cacheable, SocketGuildUser? after)
    {
        _ = Task.Factory.StartNew(async () =>
        {
            try
            {
                if (after is null)
                    return;
                if (_guildSettings.GetGuildConfig(after.Guild.Id).GameVoiceChannel != after?.VoiceChannel?.Id)
                    return;
                //if the user is in the voice channel and that voice channel is gvc
                //if the activity has changed, and is a playing activity
                if (!Equals(cacheable.Value.Activities, after.Activities)
                    && after.Activities != null
                    && after.Activities.FirstOrDefault()?.Type == ActivityType.Playing)
                {
                    //trigger gvc
                    await TriggerGvc(after, after.Activities.FirstOrDefault()?.Name).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error running GuildMemberUpdated in gvc");
            }
        }, TaskCreationOptions.LongRunning);
        return Task.CompletedTask;
    }

    public ulong? ToggleGameVoiceChannel(ulong guildId, ulong vchId)
    {
        ulong? id;
        using var uow = _db.GetDbContext();
        var gc = uow.ForGuildId(guildId, set => set);

        if (gc.GameVoiceChannel == vchId)
        {
            id = gc.GameVoiceChannel = null;
            _guildSettings.UpdateGuildConfig(guildId, gc);
        }
        else
        {
            id = gc.GameVoiceChannel = vchId;
            _guildSettings.UpdateGuildConfig(guildId, gc);
        }

        uow.SaveChanges();

        return id;
    }

    private Task Client_UserVoiceStateUpdated(SocketUser usr, SocketVoiceState oldState, SocketVoiceState newState)
    {
        var _ = Task.Factory.StartNew(async () =>
        {
            try
            {
                if (usr is not SocketGuildUser gUser)
                    return;

                var game = gUser.Activities.FirstOrDefault()?.Name;

                if (oldState.VoiceChannel == newState.VoiceChannel ||
                    newState.VoiceChannel == null)
                {
                    return;
                }

                if (_guildSettings.GetGuildConfig(gUser.Guild.Id).GameVoiceChannel != newState.VoiceChannel.Id ||
                    string.IsNullOrWhiteSpace(game))
                {
                    return;
                }

                await TriggerGvc(gUser, game).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error running VoiceStateUpdate in gvc");
            }
        }, TaskCreationOptions.LongRunning);

        return Task.CompletedTask;
    }

    private static async Task TriggerGvc(SocketGuildUser gUser, string game)
    {
        if (string.IsNullOrWhiteSpace(game))
            return;

        game = game.TrimTo(50).ToLowerInvariant();
        var vch = gUser.Guild.VoiceChannels
            .FirstOrDefault(x => x.Name.ToLowerInvariant() == game);

        if (vch == null)
            return;

        await Task.Delay(1000).ConfigureAwait(false);
        await gUser.ModifyAsync(gu => gu.Channel = vch).ConfigureAwait(false);
    }
}