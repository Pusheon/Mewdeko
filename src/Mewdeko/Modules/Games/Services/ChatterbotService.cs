using System.Collections.Concurrent;
using System.Net.Http;
using Discord;
using Discord.WebSocket;
using Mewdeko._Extensions;
using Mewdeko.Common.ModuleBehaviors;
using Mewdeko.Database;
using Mewdeko.Database.Extensions;
using Mewdeko.Modules.Games.Common.ChatterBot;
using Serilog;

namespace Mewdeko.Modules.Games.Services;

public class ChatterBotService : INService
{
    private readonly DiscordSocketClient _client;
    private readonly CommandHandler _cmd;
    private readonly IBotCredentials _creds;
    private readonly DbService _db;
    private readonly IHttpClientFactory _httpFactory;
    public List<ulong> LimitUser = new();

    public ChatterBotService(DiscordSocketClient client,
        Mewdeko bot, CommandHandler cmd, IHttpClientFactory factory,
        IBotCredentials creds, DbService db)
    {
        _db = db;
        _client = client;
        _cmd = cmd;
        _creds = creds;
        _httpFactory = factory;
        _client.MessageReceived += MessageRecieved;

        ChatterBotChannels = new ConcurrentDictionary<ulong, Lazy<IChatterBotSession>>(
            bot.AllGuildConfigs
                .Where(gc => gc.CleverbotChannel != 0)
                .ToDictionary(gc => gc.CleverbotChannel,
                    _ => new Lazy<IChatterBotSession>(() => CreateSession(), true)));
    }

    public ConcurrentDictionary<ulong, Lazy<IChatterBotSession>> ChatterBotChannels { get; }

    public static int Priority => -1;
    public static ModuleBehaviorType BehaviorType => ModuleBehaviorType.Executor;

    public async Task SetCleverbotChannel(IGuild guild, ulong id)
    {
        await using (var uow = _db.GetDbContext())
        {
            var gc = uow.ForGuildId(guild.Id, set => set);
            gc.CleverbotChannel = id;
            await uow.SaveChangesAsync();
        }

        if (id == 0)
            ChatterBotChannels.TryRemove(id, out _);
        else
            ChatterBotChannels.TryAdd(id,
                new Lazy<IChatterBotSession>(() => CreateSession(), true));
    }

    public ulong GetCleverbotChannel(ulong id) => _db.GetDbContext().GuildConfigs.GetCleverbotChannel(id);

    public Task MessageRecieved(SocketMessage usrMsg)
    {
        _ = Task.Run(async () =>
        {
            if (usrMsg.Author.IsBot)
                return;
            if (usrMsg.Channel is not ITextChannel chan)
                return;
            try
            {
                var message = PrepareMessage(usrMsg as IUserMessage, out var cbs);
                if (message == null || cbs == null)
                    return;
                if (LimitUser.Contains(chan.Id)) return;
                var cleverbotExecuted = await TryAsk(cbs, (ITextChannel) usrMsg.Channel, message).ConfigureAwait(false);
                if (cleverbotExecuted)
                {
                    Log.Information(
                        $@"CleverBot Executed
                    Server: {chan.Guild.Name} {chan.Guild.Name}]
                    Channel: {usrMsg.Channel?.Name} [{usrMsg.Channel?.Id}]
                    UserId: {usrMsg.Author} [{usrMsg.Author.Id}]
                    Message: {usrMsg.Content}");
                    LimitUser.Add(chan.Id);
                    await Task.Delay(5000);
                    LimitUser.Remove(chan.Id);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error in cleverbot");
            }
        });
        return Task.CompletedTask;
    }

    public IChatterBotSession CreateSession()
    {
        if (!string.IsNullOrWhiteSpace(_creds.CleverbotApiKey))
            return new OfficialCleverbotSession(_creds.CleverbotApiKey, _httpFactory);
        return new CleverbotIoSession("GAh3wUfzDCpDpdpT", "RStKgqn7tcO9blbrv4KbXM8NDlb7H37C", _httpFactory);
    }

    private string PrepareMessage(IUserMessage? msg, out IChatterBotSession cleverbot)
    {
        cleverbot = null;
        if (msg?.Channel is not ITextChannel channel)
            return null;

        if (!ChatterBotChannels.TryGetValue(channel.Id, out var lazyCleverbot))
            return null;

        cleverbot = lazyCleverbot.Value;

        var mewdekoId = _client.CurrentUser.Id;
        var normalMention = $"<@{mewdekoId}> ";
        var nickMention = $"<@!{mewdekoId}> ";
        string message;

        if (msg.Content.StartsWith(normalMention, StringComparison.InvariantCulture))
            message = msg.Content[normalMention.Length..].Trim();
        else if (msg.Content.StartsWith(nickMention, StringComparison.InvariantCulture))
            message = msg.Content[nickMention.Length..].Trim();
        else if (msg.Content.StartsWith(_cmd.GetPrefix(channel.Guild)))
            return null;
        else
            message = msg.Content;
        return message;
    }

    private static async Task<bool> TryAsk(IChatterBotSession cleverbot, ITextChannel channel, string message)
    {
        await channel.TriggerTypingAsync().ConfigureAwait(false);
        string response = String.Empty;
        try
        {
            response = await cleverbot.Think(message).ConfigureAwait(false);
        }
        catch
        {
            await channel.SendErrorAsync(
                "Cleverbot is paid and I cannot pay for it right now! If you want to support Mewdeko and reenable this please donate so it'll be available!\nhttps://ko-fi.com/mewdeko\nThis is not a premium feature and never will be!");
            return false;
        }
        try
        {
            await channel.SendConfirmAsync(response.SanitizeMentions(true)).ConfigureAwait(false);
        }
        catch
        {
            await channel.SendConfirmAsync(response.SanitizeMentions(true)).ConfigureAwait(false); // try twice :\
        }

        return true;
    }
}