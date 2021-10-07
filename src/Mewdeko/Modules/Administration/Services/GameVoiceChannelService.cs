﻿using System;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Mewdeko._Extensions;
using Mewdeko.Common.Collections;
using Mewdeko.Services;
using Serilog;

namespace Mewdeko.Modules.Administration.Services
{
    public class GameVoiceChannelService : INService
    {
        private readonly DiscordSocketClient _client;

        private readonly DbService _db;

        public GameVoiceChannelService(DiscordSocketClient client, DbService db, Mewdeko.Services.Mewdeko bot)
        {
            _db = db;
            _client = client;

            GameVoiceChannels = new ConcurrentHashSet<ulong>(
                bot.AllGuildConfigs.Where(gc => gc.GameVoiceChannel != null)
                    .Select(gc => gc.GameVoiceChannel.Value));

            _client.UserVoiceStateUpdated += Client_UserVoiceStateUpdated;
            _client.GuildMemberUpdated += _client_GuildMemberUpdated;
        }

        public ConcurrentHashSet<ulong> GameVoiceChannels { get; } = new();

        private Task _client_GuildMemberUpdated(Cacheable<SocketGuildUser, ulong> before, SocketGuildUser after)
        {
            var _ = Task.Run(async () =>
            {
                try
                {
                    //if the user is in the voice channel and that voice channel is gvc
                    var vc = after.VoiceChannel;
                    if (vc == null || !GameVoiceChannels.Contains(vc.Id))
                        return;

                    //if the activity has changed, and is a playing activity
                    if (before.Value.Activities != after.Activities
                        && after.Activities != null
                        && after.Activities.FirstOrDefault().Type == ActivityType.Playing)
                        //trigger gvc
                        await TriggerGvc(after, after.Activities.FirstOrDefault().Name);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Error running GuildMemberUpdated in gvc");
                }
            });
            return Task.CompletedTask;
        }

        public ulong? ToggleGameVoiceChannel(ulong guildId, ulong vchId)
        {
            ulong? id;
            using (var uow = _db.GetDbContext())
            {
                var gc = uow.GuildConfigs.ForId(guildId, set => set);

                if (gc.GameVoiceChannel == vchId)
                {
                    GameVoiceChannels.TryRemove(vchId);
                    id = gc.GameVoiceChannel = null;
                }
                else
                {
                    if (gc.GameVoiceChannel != null)
                        GameVoiceChannels.TryRemove(gc.GameVoiceChannel.Value);
                    GameVoiceChannels.Add(vchId);
                    id = gc.GameVoiceChannel = vchId;
                }

                uow.SaveChanges();
            }

            return id;
        }

        private Task Client_UserVoiceStateUpdated(SocketUser usr, SocketVoiceState oldState, SocketVoiceState newState)
        {
            var _ = Task.Run(async () =>
            {
                try
                {
                    if (!(usr is SocketGuildUser gUser))
                        return;

                    var game = gUser.Activities.FirstOrDefault()?.Name;

                    if (oldState.VoiceChannel == newState.VoiceChannel ||
                        newState.VoiceChannel == null)
                        return;

                    if (!GameVoiceChannels.Contains(newState.VoiceChannel.Id) ||
                        string.IsNullOrWhiteSpace(game))
                        return;

                    await TriggerGvc(gUser, game);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Error running VoiceStateUpdate in gvc");
                }
            });

            return Task.CompletedTask;
        }

        private async Task TriggerGvc(SocketGuildUser gUser, string game)
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
}