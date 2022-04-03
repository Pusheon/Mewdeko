using Discord;
using Discord.WebSocket;
using Mewdeko.Common.ModuleBehaviors;
using Mewdeko.Database;
using Mewdeko.Database.Extensions;
using Mewdeko.Extensions;
using Mewdeko.Modules.Games.Common.ChatterBot;
using Serilog;
using System.Collections.Concurrent;
using System.Net.Http;

namespace Mewdeko.Modules.Games.Services;

public class ChatterBotService : INService
{
    private readonly DiscordSocketClient _client;
    private readonly CommandHandler _cmd;
    private readonly IBotCredentials _creds;
    private readonly Mewdeko _bot;
    private readonly DbService _db;
    private readonly IHttpClientFactory _httpFactory;
    public List<ulong> LimitUser = new();

    public ChatterBotService(DiscordSocketClient client,
        Mewdeko bot, CommandHandler cmd, IHttpClientFactory factory,
        IBotCredentials creds, DbService db)
    {
        _db = db;
        _client = client;
        _bot = bot;
        _cmd = cmd;
        _creds = creds;
        _httpFactory = factory;
        _client.MessageReceived += MessageRecieved;
    }

    public static int Priority => -1;
    public static ModuleBehaviorType BehaviorType => ModuleBehaviorType.Executor;

    public ConcurrentDictionary<ulong, Lazy<IChatterBotSession>> CleverbotUsers = new();

    public async Task SetCleverbotChannel(IGuild guild, ulong id)
    {
        await using var uow = _db.GetDbContext();
        var gc = uow.ForGuildId(guild.Id, set => set);
        gc.CleverbotChannel = id;
        await uow.SaveChangesAsync();
        _bot.UpdateGuildConfig(guild.Id, gc);
    }

    public ulong GetCleverbotChannel(ulong id) => _bot.GetGuildConfig(id).CleverbotChannel;

    public Task MessageRecieved(SocketMessage msg)
    {
        _ = Task.Run(async () =>
        {
            if (msg.Author.IsBot)
                return;
            if (msg.Channel is not ITextChannel chan)
                return;
            try
            {
                if (msg is not IUserMessage usrMsg)
                    return;
                IChatterBotSession cbs;
                string message;
                try
                {
                    message = PrepareMessage(usrMsg, out cbs);
                }
                catch
                {
                    return;
                }
                
                if (message == null || cbs == null)
                    return;
                var cleverbotExecuted = await TryAsk(cbs, (ITextChannel) usrMsg.Channel, message, usrMsg).ConfigureAwait(false);
                if (cleverbotExecuted)
                {
                    Log.Information(
                        $@"CleverBot Executed
                    Server: {chan.Guild.Name} {chan.Guild.Name}]
                    Channel: {usrMsg.Channel?.Name} [{usrMsg.Channel?.Id}]
                    UserId: {usrMsg.Author} [{usrMsg.Author.Id}]
                    Message: {usrMsg.Content}");
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

    private string PrepareMessage(IMessage? msg, out IChatterBotSession cleverbot)
    {
        cleverbot = null;
        if (msg?.Channel is not ITextChannel channel)
            return null;
        if (GetCleverbotChannel(channel.Guild.Id) == 0)
            return null;
        if (GetCleverbotChannel(channel.Guild.Id) != channel.Id)
            return null;
        if (!CleverbotUsers.TryGetValue(msg.Author.Id, out var lazyCleverbot))
        {
            CleverbotUsers.TryAdd(msg.Author.Id, new Lazy<IChatterBotSession>(CreateSession, true));
            CleverbotUsers.TryGetValue(msg.Author.Id, out lazyCleverbot);
        }
        
      
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

    private static async Task<bool> TryAsk(IChatterBotSession cleverbot, ITextChannel channel, string message, IUserMessage msg)
    {
        await channel.TriggerTypingAsync().ConfigureAwait(false);
        string response;
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

        await msg.ReplyAsync(embed: new EmbedBuilder().WithOkColor().WithDescription(response.SanitizeMentions(true)).Build());

        return true;
    }
}