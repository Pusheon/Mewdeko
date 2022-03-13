using Discord;
using Discord.WebSocket;
using Mewdeko._Extensions;
using Mewdeko.Common.ModuleBehaviors;
using Mewdeko.Database;
using Mewdeko.Database.Extensions;
using Mewdeko.Database.Models;
using System.Threading.Channels;

namespace Mewdeko.Modules.Highlights.Services;

public class HighlightsService : INService, IReadyExecutor
{
    private readonly DiscordSocketClient _client;
    private readonly IDataCache _cache;
    private readonly DbService _db;
    

    public HighlightsService(DiscordSocketClient client, IDataCache cache, DbService db)
    {
        _client = client;
        _cache = cache;
        _db = db;
        _ = CacheToRedis();
        _client.MessageReceived += StaggerHighlights;
        _client.UserIsTyping += AddHighlightTimer;
        
    }

    private async Task AddHighlightTimer(Cacheable<IUser, ulong> arg1, Cacheable<IMessageChannel, ulong> _)
    {
        if (arg1.Value is not IGuildUser user)
            return;

        await _cache.TryAddHighlightStagger(user.GuildId, user.Id);
    }

    public async Task OnReadyAsync()
    {
        while (true)
        {
            var (msg, compl) = await _highlightQueue.Reader.ReadAsync();
            var res = await ExecuteHighlights(msg);
            compl.TrySetResult(res);
            await Task.Delay(2000);
        }
    }

    private Task StaggerHighlights(SocketMessage message)
    {
        _ = Task.Run(async () =>
        {
            var completionSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            await _highlightQueue.Writer.WriteAsync((message, completionSource));
            return await completionSource.Task;
        });
        return Task.CompletedTask;
    }


    private readonly Channel<(SocketMessage, TaskCompletionSource<bool>)> _highlightQueue =
        Channel.CreateBounded<(SocketMessage, TaskCompletionSource<bool>)>(new BoundedChannelOptions(60)
        {
            FullMode = BoundedChannelFullMode.DropNewest
        });


    private async Task<bool> ExecuteHighlights(SocketMessage message)
    {
        if (message.Channel is not ITextChannel channel)
            return true;
        
        if (string.IsNullOrWhiteSpace(message.Content))
            return true;
        var usersDMd = new List<ulong>();
        var splitwords = message.Content.Split(" ");
        var highlightWords = GetForGuild(channel.Guild.Id);
        if (!highlightWords.Any())
            return true;
        var toSend = (from i in splitwords from j in highlightWords where String.Equals(j.Word, i, StringComparison.CurrentCultureIgnoreCase) select j).ToList();
        foreach (var i in toSend)
        {
            if (await _cache.GetHighlightStagger(channel.Guild.Id, i.UserId))
                continue;
            if (usersDMd.Contains(i.UserId))
                continue;
            if (GetSettingsForGuild(channel.GuildId).Any())
            {
                var settings = GetSettingsForGuild(channel.GuildId)
                    .FirstOrDefault(x => x.UserId == i.UserId && x.GuildId == channel.GuildId);
                if (settings is not null)
                {
                    if (!settings.HighlightsOn)
                        continue;
                    if (settings.IgnoredChannels.Split(" ").Contains(channel.Id.ToString()))
                        continue;
                    if (settings.IgnoredUsers.Split(" ").Contains(message.Author.Id.ToString()))
                        continue;
                }
            }

            if (!await _cache.TryAddHighlightStaggerUser(i.UserId))
                continue;
            var user = await channel.Guild.GetUserAsync(i.UserId);
            var permissions = user.GetPermissions(channel);
            if (!permissions.ViewChannel)
                continue;
            var messages = await channel.GetMessagesAsync(message.Id, Direction.Before, 5).FlattenAsync();
            var eb = new EmbedBuilder().WithOkColor().WithTitle(i.Word.TrimTo(100)).WithDescription(string.Join("\n",
                                           messages.OrderBy(x => x.Timestamp).Select(x => $"**[{x.Timestamp:h:mm:ss}]**: {Format.Bold(x.Author.ToString())}: {x.Content?.TrimTo(100)}")))
                                       .WithFooter("Yes this was shamelessly copied from carl-bot.");
            await user.SendMessageAsync(
                $"In {Format.Bold(channel.Guild.Name)} {channel.Mention} you were mentioned with highlight word {i.Word}",
                embed: eb.Build());
            usersDMd.Add(user.Id);

        }

        return true;
    }

    private async Task CacheToRedis()
    {
        await using var uow = _db.GetDbContext();
        var allHighlights = uow.Highlights.AllHighlights();
        var allHighlightSettings = uow.HighlightSettings.AllHighlightSettings();
        var guilds = await _client.Rest.GetGuildsAsync();
        foreach (var i in guilds)
        {
            await _cache.CacheHighlights(i.Id, allHighlights.Where(x => x.GuildId == i.Id).ToList());
            await _cache.CacheHighlightSettings(i.Id, allHighlightSettings.Where(x => x.GuildId == i.Id).ToList());
        }
    }

    public async Task AddHighlight(ulong guildId, ulong userId, string word)
    {
        var toadd = new Database.Models.Highlights { UserId = userId, GuildId = guildId, Word = word };
        await using var uow = _db.GetDbContext();
        uow.Highlights.Add(toadd);
        var current = _cache.GetHighlightsForGuild(guildId) ?? new List<Database.Models.Highlights>();
        current.Add(toadd);
        await _cache.AddHighlightToCache(guildId, current);
    }

    public async Task ToggleHighlights(ulong guildId, ulong userId, bool enabled)
    {
        await using var uow = _db.GetDbContext();
        var toupdate = uow.HighlightSettings.FirstOrDefault(x => x.UserId == userId && x.GuildId == guildId);
        if (toupdate is null)
        {
            var toadd = new HighlightSettings()
            {
                GuildId = guildId,
                UserId = userId,
                HighlightsOn = enabled,
                IgnoredChannels = "0",
                IgnoredUsers = "0"
            };
            uow.HighlightSettings.Add(toadd);
            await uow.SaveChangesAsync();
            var current1 = _cache.GetHighlightSettingsForGuild(guildId) ?? new List<HighlightSettings>();
            current1.Add(toadd);
            await _cache.AddHighlightSettingToCache(guildId, current1);
        }

        toupdate.HighlightsOn = enabled;
        uow.HighlightSettings.Update(toupdate);
        await uow.SaveChangesAsync();
        var current = _cache.GetHighlightSettingsForGuild(guildId) ?? new List<HighlightSettings>();
        current.Add(toupdate);
        await _cache.AddHighlightSettingToCache(guildId, current);
    }
    public async Task<bool> ToggleIgnoredChannel(ulong guildId, ulong userId, string channelId)
    {
        bool ignored = true;
        await using var uow = _db.GetDbContext();
        var toupdate = uow.HighlightSettings.FirstOrDefault(x => x.UserId == userId && x.GuildId == guildId);
        if (toupdate is null)
        {
            var toadd = new HighlightSettings()
            {
                GuildId = guildId,
                UserId = userId,
                HighlightsOn = true,
                IgnoredChannels = "0",
                IgnoredUsers = "0"
            };
            var toedit1 = toadd.IgnoredUsers.Split(" ").ToList(); 
            toedit1.Add(channelId);
            toadd.IgnoredChannels = string.Join(" ", toedit1);
            uow.HighlightSettings.Add(toadd);
            await uow.SaveChangesAsync();
            var current1 = _cache.GetHighlightSettingsForGuild(guildId) ?? new List<HighlightSettings>();
            current1.Add(toadd);
            await _cache.AddHighlightSettingToCache(guildId, current1);
            return ignored;
        }
        var toedit = toupdate.IgnoredChannels.Split(" ").ToList();
        if (toedit.Contains(channelId))
        {
            toedit.Remove(channelId);
            ignored = false;
        }
        else
            toedit.Add(channelId);
        toupdate.IgnoredChannels = string.Join(" ", toedit);
        uow.HighlightSettings.Update(toupdate);
        await uow.SaveChangesAsync();
        var current = _cache.GetHighlightSettingsForGuild(guildId) ?? new List<HighlightSettings>();
        current.Add(toupdate);
        await _cache.AddHighlightSettingToCache(guildId, current);
        return ignored;
    }
    
    public async Task<bool> ToggleIgnoredUser(ulong guildId, ulong userId, string userToIgnore)
    {
        bool ignored = true;
        await using var uow = _db.GetDbContext();
        var toupdate = uow.HighlightSettings.FirstOrDefault(x => x.UserId == userId && x.GuildId == guildId);
        if (toupdate is null)
        {
            var toadd = new HighlightSettings()
            {
                GuildId = guildId,
                UserId = userId,
                HighlightsOn = true,
                IgnoredChannels = "0",
                IgnoredUsers = "0"
            };
            var toedit1 = toadd.IgnoredUsers.Split(" ").ToList(); 
            toedit1.Add(userToIgnore);
            toadd.IgnoredUsers = string.Join(" ", toedit1);
            uow.HighlightSettings.Add(toadd);
            await uow.SaveChangesAsync();
            var current1 = _cache.GetHighlightSettingsForGuild(guildId) ?? new List<HighlightSettings>();
            current1.Add(toadd);
            await _cache.AddHighlightSettingToCache(guildId, current1);
            return ignored;
        }
        var toedit = toupdate.IgnoredUsers.Split(" ").ToList();
        if (toedit.Contains(userToIgnore))
        {
            toedit.Remove(userToIgnore);
            ignored = false;
        }
        else
            toedit.Add(userToIgnore);
        toupdate.IgnoredUsers = string.Join(" ", toedit);
        uow.HighlightSettings.Update(toupdate);
        await uow.SaveChangesAsync();
        var current = _cache.GetHighlightSettingsForGuild(guildId) ?? new List<HighlightSettings>();
        current.Add(toupdate);
        await _cache.AddHighlightSettingToCache(guildId, current);
        return ignored;
    }
    
    public async Task RemoveHighlight(ulong guildId, ulong userId, string word)
    {
        var toremove = new Database.Models.Highlights { UserId = userId, GuildId = guildId, Word = word };
        await using var uow = _db.GetDbContext();
        uow.Highlights.Remove(toremove);
        var current = _cache.GetHighlightsForGuild(guildId) ?? new List<Database.Models.Highlights>();
        if (current.Any())
            current.Remove(toremove);
        await _cache.RemoveHighlightFromCache(guildId, current);
    }

    public List<Database.Models.Highlights> GetForGuild(ulong guildId) 
        => _cache.GetHighlightsForGuild(guildId) ?? new List<Database.Models.Highlights>();

    public List<HighlightSettings> GetSettingsForGuild(ulong guildId) 
        => _cache.GetHighlightSettingsForGuild(guildId) ?? new List<HighlightSettings>();


}