using System.Collections.Concurrent;
using System.Net.Http;
using Discord;
using Discord.WebSocket;
using Mewdeko.Database.Models;
using Mewdeko.Modules.Gambling.Common.Events;
using Serilog;

namespace Mewdeko.Modules.Gambling.Services;

public class CurrencyEventsService : INService
{
    private readonly DiscordSocketClient _client;
    private readonly GamblingConfigService _configService;
    private readonly IBotCredentials _creds;
    private readonly ICurrencyService _cs;

    private readonly ConcurrentDictionary<ulong, ICurrencyEvent> _events =
        new();

    private readonly IHttpClientFactory _http;

    public CurrencyEventsService(DiscordSocketClient client,
        IBotCredentials creds, ICurrencyService cs,
        IHttpClientFactory http, GamblingConfigService configService)
    {
        _client = client;
        _cs = cs;
        _creds = creds;
        _http = http;
        _configService = configService;
    }

    public async Task<bool> TryCreateEventAsync(ulong guildId, ulong channelId, CurrencyEvent.Type type,
        EventOptions opts, Func<CurrencyEvent.Type, EventOptions, long, EmbedBuilder> embed)
    {
        var g = _client.GetGuild(guildId);
        if (g?.GetChannel(channelId) is not SocketTextChannel ch)
            return false;

        ICurrencyEvent ce;

        switch (type)
        {
            case CurrencyEvent.Type.Reaction:
                ce = new ReactionEvent(_client, _cs, g, ch, opts, _configService.Data, embed);
                break;
            case CurrencyEvent.Type.GameStatus:
                ce = new GameStatusEvent(_client, _cs, g, ch, opts, embed);
                break;
            default:
                return false;
        }

        var added = _events.TryAdd(guildId, ce);
        if (added)
            try
            {
                ce.OnEnded += OnEventEnded;
                await ce.StartEvent().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error starting event");
                _events.TryRemove(guildId, out ce);
                return false;
            }

        return added;
    }

    private Task OnEventEnded(ulong gid)
    {
        _events.TryRemove(gid, out _);
        return Task.CompletedTask;
    }

    public class VoteModel
    {
        public ulong User { get; set; }
        public long Date { get; set; }
    }
}