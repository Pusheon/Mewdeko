using Discord;
using Discord.WebSocket;
using Lavalink4NET;
using Lavalink4NET.Player;
using Lavalink4NET.Rest;
using Mewdeko._Extensions;
using Mewdeko.Database;
using Mewdeko.Database.Extensions;
using Mewdeko.Database.Models;
using SpotifyAPI.Web;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace Mewdeko.Modules.Music.Services;

public class MusicService : INService
    {
        public readonly ConcurrentDictionary<ulong, List<LavalinkTrack>> Queues;
        private readonly ConcurrentDictionary<ulong, MusicPlayerSettings> _settings;
        private readonly DbService _db;
        private readonly LavalinkNode _lavaNode;
        private readonly IBotCredentials _creds;
        
        public MusicService(LavalinkNode lavaNode, IBotCredentials creds, DbService db, DiscordSocketClient client)
        {
            _lavaNode = lavaNode;
            _creds = creds;
            _db = db;
            _settings = new ConcurrentDictionary<ulong, MusicPlayerSettings>();
            Queues = new ConcurrentDictionary<ulong, List<LavalinkTrack>>();
            client.UserVoiceStateUpdated += HandleDisconnect;
        }

        public async Task<string> GetPrettyInfo(LavalinkPlayer player, IGuild guild)
        {
            var currentTrack = player.CurrentTrack;
            var currentContext = currentTrack.Context as MusicPlayer.AdvancedTrackContext;
            var musicSettings = await GetSettingsInternalAsync(guild.Id);
            return
                $"{player.Position.Position:hh\\:mm\\:ss}/{currentTrack.Duration:hh\\:mm\\:ss} | {currentContext.QueueUser} | {currentContext.QueuedPlatform} | Vol: {musicSettings.Volume} | Loop: {musicSettings.PlayerRepeat} | {GetQueue(guild.Id).Count} tracks in queue";

        }
        public async Task UpdateDefaultPlaylist(IUser user, MusicPlaylist mpl)
        {
            await using var uow = _db.GetDbContext();
            var def = uow.MusicPlaylists.GetDefaultPlaylist(user.Id);
            if (def != null)
            {
                var toupdate = new MusicPlaylist
                {
                    AuthorId = def.AuthorId,
                    Author = def.Author,
                    DateAdded = def.DateAdded,
                    Id = def.Id,
                    IsDefault = false,
                    Name = def.Name,
                    Songs = def.Songs
                };
                uow.MusicPlaylists.Update(toupdate);
            }
            var toupdate1 = new MusicPlaylist
            {
                AuthorId = mpl.AuthorId,
                Author = mpl.Author,
                DateAdded = mpl.DateAdded,
                Id = mpl.Id,
                IsDefault = true,
                Name = mpl.Name,
                Songs = mpl.Songs
            };
            uow.MusicPlaylists.Update(toupdate1);
            await uow.SaveChangesAsync();
        }
        public Task Enqueue(
            ulong guildId,
            IUser user,
            LavalinkTrack[] lavaTracks,
            Platform queuedPlatform = Platform.Youtube)
        {
            var queue = Queues.GetOrAdd(guildId, new List<LavalinkTrack>());
            foreach (var i in lavaTracks)
            {
                i.Context = new MusicPlayer.AdvancedTrackContext(user, queuedPlatform);
                queue.Add(i);
            }

            return Task.CompletedTask;
        }

        public Task Enqueue(
            ulong guildId,
            IUser user,
            LavalinkTrack? lavaTrack,
            Platform queuedPlatform = Platform.Youtube)
        {
            var queue = Queues.GetOrAdd(guildId, new List<LavalinkTrack>());
            lavaTrack.Context = new MusicPlayer.AdvancedTrackContext(user, queuedPlatform);
            queue.Add(lavaTrack);
            return Task.CompletedTask;
        }

        public async Task SpotifyQueue(
            IGuild guild,
            IUser user,
            ITextChannel? chan,
            LavalinkPlayer player,
            string? uri)
        {
            Debug.Assert(uri != null, $"{nameof(uri)} != null");
            var spotifyUrl = new Uri(uri);
            switch (spotifyUrl.Segments[1])
            {
                case "playlist/":
                    if (_creds.SpotifyClientId is null or "")
                    {
                        await chan.SendErrorAsync(
                            "Looks like the owner of this bot hasnt added the spotify Id and CLient Secret to their credentials. Spotify queueing wont work without this.");
                        return;
                    }

                    var result = await (await GetSpotifyClient()).Playlists.Get(spotifyUrl.Segments[2]);
                    if (result.Tracks != null && result.Tracks.Items!.Any())
                    {
                        var items = result.Tracks.Items;
                        var eb = new EmbedBuilder()
                                 .WithAuthor("Spotify Playlist",
                                     "https://assets.stickpng.com/images/5ece5029123d6d0004ce5f8b.png").WithOkColor()
                                 .WithDescription($"Trying to queue {items!.Count} tracks from {result.Name}...")
                                 .WithThumbnailUrl(result.Images?.FirstOrDefault()?.Url);
                        var msg = await chan!.SendMessageAsync(embed: eb.Build());
                        var addedcount = 0;
                        foreach (var track in items.Select(i => i.Track as FullTrack))
                        {
                            if (player.State is PlayerState.Destroyed or PlayerState.NotConnected)
                                return;
                            var lavaTrack = await _lavaNode.GetTrackAsync(
                                $"{track?.Name} {track?.Artists.FirstOrDefault()?.Name}", SearchMode.YouTube);
                            if (lavaTrack is null) continue;
                            await Enqueue(guild.Id, user, lavaTrack, Platform.Spotify);
                            if (player.State != PlayerState.Playing && player.State != PlayerState.Destroyed)
                            {
                                await player.PlayAsync(lavaTrack);
                                await player.SetVolumeAsync((await GetVolume(guild.Id)) / 100.0F);
                                await ModifySettingsInternalAsync(guild.Id,
                                    (settings, _) => settings.MusicChannelId = chan.Id, chan.Id);
                            }

                            addedcount++;
                        }

                        if (addedcount == 0)
                        {
                            eb.WithErrorColor()
                              .WithDescription(
                                  $"Seems like I couldn't load any tracks from {result.Name}... Perhaps its private?");
                            await msg.ModifyAsync(x => x.Embed = eb.Build());
                        }

                        eb.WithDescription($"Successfully queued {addedcount} tracks!");
                        await msg.ModifyAsync(x => x.Embed = eb.Build());
                    }

                    break;
                case "album/":
                    if (_creds.SpotifyClientId == "")
                    {
                        await chan.SendErrorAsync(
                            "Looks like the owner of this bot hasnt added the spotify Id and CLient Secret to their credentials. Spotify queueing wont work without this.");
                        return;
                    }

                    var result1 = await (await GetSpotifyClient()).Albums.Get(spotifyUrl.Segments[2]);
#pragma warning disable CS8629 // Nullable value type may be null.
                    if ((bool)result1.Tracks.Items?.Any())
#pragma warning restore CS8629 // Nullable value type may be null.
                    {
                        var items = result1.Tracks.Items;
                        var eb = new EmbedBuilder()
                                 .WithAuthor("Spotify Album",
                                     "https://assets.stickpng.com/images/5ece5029123d6d0004ce5f8b.png").WithOkColor()
                                 .WithDescription($"Trying to queue {items.Count} tracks from {result1.Name}...")
                                 .WithThumbnailUrl(result1.Images.FirstOrDefault()?.Url);
                        var msg = await chan!.SendMessageAsync(embed: eb.Build());
                        var addedcount = 0;
                        foreach (var track in items)
                        {
                            if (player.State is PlayerState.Destroyed or PlayerState.NotConnected)
                                return;
                            var lavaTrack = await _lavaNode.GetTrackAsync(
                                $"{track.Name} {track.Artists.FirstOrDefault()?.Name}");
                            if (lavaTrack is null) continue;
                            await Enqueue(guild.Id, user, lavaTrack, Platform.Spotify);
                            if (player.State != PlayerState.Playing)
                            {
                                await player.PlayAsync(lavaTrack);
                                await player.SetVolumeAsync((await GetVolume(guild.Id)) / 100.0F);
                                await ModifySettingsInternalAsync(guild.Id,
                                    (settings, _) => settings.MusicChannelId = chan.Id, chan.Id);
                            }

                            addedcount++;
                        }

                        if (addedcount == 0)
                        {
                            eb.WithErrorColor()
                              .WithDescription(
                                  $"Seems like I couldn't load any tracks from {result1.Name}... Perhaps the songs weren't found or are exclusive?");
                            await msg.ModifyAsync(x => x.Embed = eb.Build());
                        }

                        eb.WithDescription($"Successfully queued {addedcount} tracks!").WithTitle(result1.Name);
                        await msg.ModifyAsync(x => x.Embed = eb.Build());
                    }

                    break;

                case "track/":
                    if (_creds.SpotifyClientId == "")
                    {
                        await chan.SendErrorAsync(
                            "Looks like the owner of this bot hasnt added the spotify Id and CLient Secret to their credentials. Spotify queueing wont work without this.");
                        return;
                    }
                    
                    var result3 = await (await GetSpotifyClient()).Tracks.Get(spotifyUrl.Segments[2]);
                    if (result3.Name is null)
                    {
                        await chan.SendErrorAsync(
                            "Seems like i can't find or play this. Please try with a different link!");
                        return;
                    }
                    
                    var lavaTrack3 = await _lavaNode.GetTrackAsync(
                        $"{result3.Name} {result3.Artists.FirstOrDefault()?.Name}", SearchMode.YouTube);
                    if (player.State is PlayerState.Destroyed or PlayerState.NotConnected)
                        return;
                    await Enqueue(guild.Id, user, lavaTrack3, Platform.Spotify);
                    if (player.State != PlayerState.Playing )
                    {
                        await player.PlayAsync(lavaTrack3);
                        await player.SetVolumeAsync(await GetVolume(guild.Id) / 100.0F);
                        await ModifySettingsInternalAsync(guild.Id, (settings, _) => settings.MusicChannelId = chan.Id,
                            chan.Id);
                    }

                    break;
                default:
                    await chan.SendErrorAsync("Seems like that isn't supported at the moment!");
                    break;
            }
        }
        public LavalinkTrack GetCurrentTrack(LavalinkPlayer player, IGuild guild)
        {
            var queue = GetQueue(guild.Id);
            return queue.FirstOrDefault(x => x.Identifier == player.CurrentTrack.Identifier);
        }

        public async Task<bool> RemoveSong(IGuild guild, int trackNum)
        {
            var queue = GetQueue(guild.Id);
            if (!queue.Any())
                return false;
            var player = _lavaNode.GetPlayer(guild.Id);
            var toRemove = queue.ElementAt(trackNum - 1);
            var curTrack = GetCurrentTrack(player, guild);
            if (toRemove is null)
                return false;
            var toReplace = queue.ElementAt(queue.IndexOf(curTrack) + 1);
            if (curTrack == toRemove && toReplace is not null)
                await player.PlayAsync(toReplace);
            else
            {
                await player.StopAsync();
            }

            Queues[guild.Id].Remove(queue.ElementAt(trackNum - 1));
            return true;
        }

        public List<LavalinkTrack> GetQueue(ulong guildid) =>
            !Queues.Select(x => x.Key).Contains(guildid)
                ? new List<LavalinkTrack> { Capacity = 0 }
                : Queues.FirstOrDefault(x => x.Key == guildid).Value;
        
        private async Task<SpotifyClient> GetSpotifyClient()
        {
            var config = SpotifyClientConfig.CreateDefault();
            var request =
                new ClientCredentialsRequest(_creds.SpotifyClientId, _creds.SpotifyClientSecret);
            var response = await new OAuthClient(config).RequestToken(request);
            return new SpotifyClient(config.WithToken(response.AccessToken));
        }
        
        public async Task Skip(IGuild guild, ITextChannel? chan, LavalinkPlayer player, IInteractionContext? ctx = null, int num = 1)
        {
            var queue = GetQueue(guild.Id);
            if (queue.Any())
            {
                LavalinkTrack nextTrack;
                var currentTrack = queue.FirstOrDefault(x => player.CurrentTrack.Identifier == x.Identifier);
                try
                {
                    nextTrack = queue.ElementAt(queue.IndexOf(currentTrack) + num);
                }
                catch
                {
                    if (ctx is not null)
                    {
                        await ctx.Interaction.SendErrorAsync("This is the last/only track!");
                        return;
                    }
                    await chan.SendErrorAsync("This is the last/only track!");
                    return;
                }
                if ((await GetSettingsInternalAsync(guild.Id)).PlayerRepeat == PlayerRepeatType.Track)
                {
                    await player.PlayAsync(currentTrack);
                    if (ctx is not null)
                        await ctx.Interaction.SendConfirmAsync(
                            "Because of the repeat type I am replaying the current song!");
                    return;
                }

                await player.PlayAsync(nextTrack);
                if (ctx is not null)
                    await ctx.Interaction.SendConfirmAsync("Playing the next track.");
            }
        }
        
        private async Task HandleDisconnect(SocketUser user, SocketVoiceState before, SocketVoiceState after)
        {
            var player = _lavaNode.GetPlayer(before.VoiceChannel?.Guild.Id ?? after.VoiceChannel.Guild.Id);
            if (before.VoiceChannel is not null && player is not null)
                if (before.VoiceChannel.Users.Count == 1 &&
                    (await GetSettingsInternalAsync(before.VoiceChannel.Guild.Id)).AutoDisconnect is AutoDisconnect.Either
                        or AutoDisconnect.Voice)
                    try
                    {
                        await player.StopAsync(true);
                        await QueueClear(before.VoiceChannel.Guild.Id);
                    }
                    catch
                    {
                        // ignored
                    }
        }
        
        public void Shuffle(IGuild guild) => 
            Queues[guild.Id] = Queues[guild.Id].Shuffle().ToList();
        
        public Task QueueClear(ulong guildid)
        {
            Queues[guildid].Clear();
            return Task.CompletedTask;
        }
        public async Task<int> GetVolume(ulong guildid) => (await GetSettingsInternalAsync(guildid)).Volume;
        
        public MusicPlaylist GetDefaultPlaylist(IUser user)
        {
            using var uow = _db.GetDbContext();
            return uow.MusicPlaylists.GetDefaultPlaylist(user.Id);
        }
        public IEnumerable<MusicPlaylist> GetPlaylists(IUser user)
        {
            using var uow = _db.GetDbContext();
            var a = uow.MusicPlaylists.GetPlaylistsByUser(user.Id);
            return a.ToList();
        }
        public async Task<MusicPlayerSettings> GetSettingsInternalAsync(ulong guildId)
        {
            if (_settings.TryGetValue(guildId, out var settings))
                return settings;

            await using var uow = _db.GetDbContext();
            var toReturn = _settings[guildId] = await uow.MusicPlayerSettings.ForGuildAsync(guildId);
            await uow.SaveChangesAsync();

            return toReturn;
        }

        public async Task ModifySettingsInternalAsync<TState>(
            ulong guildId,
            Action<MusicPlayerSettings, TState> action,
            TState state)
        {
            await using var uow = _db.GetDbContext();
            var ms = await uow.MusicPlayerSettings.ForGuildAsync(guildId);
            action(ms, state);
            await uow.SaveChangesAsync();
            _settings[guildId] = ms;
        }
    }