using System.Collections.Concurrent;
using Discord;
using Discord.WebSocket;
using Lavalink4NET;
using Lavalink4NET.Artwork;
using Lavalink4NET.Events;
using Lavalink4NET.Player;
using Mewdeko._Extensions;
using Mewdeko.Database;
using Mewdeko.Database.Models;


#nullable enable

namespace Mewdeko.Modules.Music.Services;

public class MusicPlayer : LavalinkPlayer
{
    private readonly DbService _db;
    private readonly LavalinkNode _lavaNode;
    private DiscordSocketClient _client;
    private MusicService _musicService;
    private readonly ConcurrentDictionary<ulong, List<LavalinkTrack>> _queues;
    private readonly IBotCredentials _creds;
    private readonly ConcurrentDictionary<ulong, MusicPlayerSettings> _settings;

    public class AdvancedTrackContext
    {
        public AdvancedTrackContext(IUser queueUser, Platform queuedPlatform = Platform.Youtube)
        {
            QueueUser = queueUser;
            QueuedPlatform = queuedPlatform;
        }
        public IUser QueueUser { get; }
        public Platform QueuedPlatform { get; }
    }
    
    public MusicPlayer(LavalinkNode lava, DbService db, DiscordSocketClient client,
        IBotCredentials creds,
        MusicService musicService)
    {
        _db = db;
        _client = client;
        this._creds = creds;
        _musicService = musicService;
        _lavaNode = lava;
        _settings = new ConcurrentDictionary<ulong, MusicPlayerSettings>();
        _queues = new ConcurrentDictionary<ulong, List<LavalinkTrack>>();
    }

    public override async Task OnTrackStartedAsync(TrackStartedEventArgs args)
    {
        var queue = _musicService.GetQueue(args.Player.GuildId);
        var track = queue.FirstOrDefault(x => x.Identifier == args.Player.CurrentTrack.Identifier);
        LavalinkTrack nextTrack = null;
        try
        {
            nextTrack = queue.ElementAt(queue.IndexOf(track) + 1);
        }
        catch 
        {
           //ignored
        }
        var resultMusicChannelId = (await _musicService.GetSettingsInternalAsync(args.Player.GuildId)).MusicChannelId;
        if (resultMusicChannelId != null)
        {
            if (_client.GetChannel(
                    resultMusicChannelId.Value) is SocketTextChannel channel)
            {
                if (track.Source != null)
                {
                    using var artworkService = new ArtworkService();
                    var artWork = await artworkService.ResolveAsync(track);
                    var currentContext = track.Context as AdvancedTrackContext;
                    var eb = new EmbedBuilder()
                             .WithOkColor()
                             .WithDescription($"Now playing {track.Title} by {track.Author}")
                             .WithTitle($"Track #{queue.IndexOf(track)+1}")
                             .WithFooter(await _musicService.GetPrettyInfo(args.Player, _client.GetGuild(args.Player.GuildId)))
                             .WithThumbnailUrl(artWork?.AbsoluteUri);
                    if (nextTrack is not null) eb.AddField("Up Next", $"{nextTrack.Title} by {nextTrack.Author}");

                    await channel.SendMessageAsync(embed: eb.Build());
                }
            }
        }
    }

    public override async Task OnTrackEndAsync(TrackEndEventArgs args)
    {
        var queue = _musicService.GetQueue(args.Player.GuildId);
        if (queue.Any())
        {
            var gid = args.Player.GuildId;
            var msettings = await _musicService.GetSettingsInternalAsync(gid);
            var channel = _client.GetChannel(msettings.MusicChannelId!.Value) as ITextChannel;
            if (args.Reason is TrackEndReason.Stopped or TrackEndReason.CleanUp or TrackEndReason.Replaced) return;
            var currentTrack = queue.FirstOrDefault(x => args.Player.CurrentTrack.Identifier == x.Identifier);
            if (msettings.PlayerRepeat == PlayerRepeatType.Track)
            {
                await args.Player.PlayAsync(currentTrack);
                return;
            }
            
            var nextTrack = queue.ElementAt(queue.IndexOf(currentTrack) + 1);
            if (nextTrack.Source is null && channel != null)
            {
                if (msettings.PlayerRepeat == PlayerRepeatType.Queue)
                {
                    await args.Player.PlayAsync(_musicService.GetQueue(gid).FirstOrDefault());
                    return;
                }
                var eb1 = new EmbedBuilder()
                          .WithOkColor()
                          .WithDescription("I have reached the end of the queue!");
                await channel.SendMessageAsync(embed: eb1.Build());
                if ((await _musicService.GetSettingsInternalAsync(args.Player.GuildId)).AutoDisconnect is
                    AutoDisconnect.Either or AutoDisconnect.Queue)
                {
                    await args.Player.StopAsync(true);
                    return;
                }
            }

            await args.Player.PlayAsync(nextTrack);
        }
    }
}