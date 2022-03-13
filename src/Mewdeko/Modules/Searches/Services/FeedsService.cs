using System.Collections.Concurrent;
using CodeHollow.FeedReader;
using CodeHollow.FeedReader.Feeds;
using Discord;
using Discord.WebSocket;
using Humanizer;
using Mewdeko._Extensions;
using Mewdeko.Common;
using Mewdeko.Common.Replacements;
using Mewdeko.Database;
using Mewdeko.Database.Extensions;
using Mewdeko.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace Mewdeko.Modules.Searches.Services;

public class FeedsService : INService
{
    private readonly DiscordSocketClient _client;
    private readonly DbService _db;

    private readonly ConcurrentDictionary<string, DateTime> _lastPosts =
        new();

    private readonly ConcurrentDictionary<string, HashSet<FeedSub>> _subs;

    public FeedsService(Mewdeko bot, DbService db, DiscordSocketClient client)
    {
        _db = db;

        using (var uow = db.GetDbContext())
        {
            var guildConfigIds = bot.AllGuildConfigs.Select(x => x.Id).ToList();
            _subs = uow.GuildConfigs
                .AsQueryable()
                .Where(x => guildConfigIds.Contains(x.Id))
                .Include(x => x.FeedSubs)
                .ToList()
                .SelectMany(x => x.FeedSubs)
                .GroupBy(x => x.Url.ToLower())
                .ToDictionary(x => x.Key, x => x.ToHashSet())
                .ToConcurrent();
        }

        _client = client;

        var _ = Task.Run(TrackFeeds);
    }

    public async Task<EmbedBuilder> TrackFeeds()
    {
        while (true)
        {
            var allSendTasks = new List<Task>(_subs.Count);
            foreach (var (rssUrl, value) in _subs)
            {
                if (value.Count == 0)
                    continue;

                try
                {
                    var feed = await FeedReader.ReadAsync(rssUrl).ConfigureAwait(false);

                    var items = feed
                        .Items
                        .Select(item => (Item: item, LastUpdate: item.PublishingDate?.ToUniversalTime()
                                                                 ?? (item.SpecificItem as AtomFeedItem)?.UpdatedDate
                                                                 ?.ToUniversalTime()))
                        .Where(data => data.LastUpdate is not null)
                        .Select(data => (data.Item, LastUpdate: (DateTime) data.LastUpdate))
                        .OrderByDescending(data => data.LastUpdate)
                        .Reverse() // start from the oldest
                        .ToList();

                    if (!_lastPosts.TryGetValue(rssUrl, out var lastFeedUpdate))
                        lastFeedUpdate = _lastPosts[rssUrl] =
                            items.Any() ? items[^1].LastUpdate : DateTime.UtcNow;

                    foreach (var (feedItem, itemUpdateDate) in items)
                    {
                        var repbuilder = new ReplacementBuilder()
                            .WithOverride("%title%", () => feedItem.Title ?? "Unkown")
                            .WithOverride("%author%", () => feedItem.Author ?? "Unknown")
                            .WithOverride("%content%", () => feedItem.Description?.StripHtml())
                            .WithOverride("%image_url%", () =>
                            {
                                if (feedItem.SpecificItem is AtomFeedItem afi)
                                {
                                    var previewElement = afi.Element.Elements()
                                                            .FirstOrDefault(x => x.Name.LocalName == "preview");

                                    if (previewElement == null)
                                        previewElement = afi.Element.Elements()
                                                            .FirstOrDefault(x => x.Name.LocalName == "thumbnail");

                                    if (previewElement != null)
                                    {
                                        var urlAttribute = previewElement.Attribute("url");
                                        if (urlAttribute != null && !string.IsNullOrWhiteSpace(urlAttribute.Value)
                                                                 && Uri.IsWellFormedUriString(urlAttribute.Value,
                                                                     UriKind.Absolute))
                                        {
                                            return urlAttribute.Value;
                                        }
                                    }
                                }
                                if (feedItem.SpecificItem is not MediaRssFeedItem mrfi
                                    || (!(mrfi.Enclosure?.MediaType?.StartsWith("image/") ?? false)))
                                    return feed.ImageUrl;
                                var imgUrl = mrfi.Enclosure.Url;
                                if (!string.IsNullOrWhiteSpace(imgUrl) &&
                                    Uri.IsWellFormedUriString(imgUrl, UriKind.Absolute))
                                {
                                    return imgUrl;
                                }

                                return feed.ImageUrl;

                            })
                            .WithOverride("%categories%", () => string.Join(", ", feedItem.Categories))
                            .WithOverride("%timestamp%", () => TimestampTag.FromDateTime(feedItem.PublishingDate.Value, TimestampTagStyles.LongDateTime).ToString())
                            .WithOverride("%url%", () => feedItem.Link)
                            .WithOverride("%feedurl%", () => rssUrl)
                            .Build();

                        if (itemUpdateDate <= lastFeedUpdate) continue;
                        var embed = new EmbedBuilder()
                            .WithFooter(rssUrl);

                        _lastPosts[rssUrl] = itemUpdateDate;

                        var link = feedItem.SpecificItem.Link;
                        if (!string.IsNullOrWhiteSpace(link) && Uri.IsWellFormedUriString(link, UriKind.Absolute))
                            embed.WithUrl(link);

                        var title = string.IsNullOrWhiteSpace(feedItem.Title)
                            ? "-"
                            : feedItem.Title;

                        var gotImage = false;
                        if (feedItem.SpecificItem is MediaRssFeedItem mrfi &&
                            (mrfi.Enclosure?.MediaType?.StartsWith("image/") ?? false))
                        {
                            var imgUrl = mrfi.Enclosure.Url;
                            if (!string.IsNullOrWhiteSpace(imgUrl) &&
                                Uri.IsWellFormedUriString(imgUrl, UriKind.Absolute))
                            {
                                embed.WithImageUrl(imgUrl);
                                gotImage = true;
                            }
                        }

                        if (!gotImage && feedItem.SpecificItem is AtomFeedItem afi)
                        {
                            var previewElement = afi.Element.Elements()
                                .FirstOrDefault(x => x.Name.LocalName == "preview");

                            if (previewElement == null)
                                previewElement = afi.Element.Elements()
                                    .FirstOrDefault(x => x.Name.LocalName == "thumbnail");

                            if (previewElement != null)
                            {
                                var urlAttribute = previewElement.Attribute("url");
                                if (urlAttribute != null && !string.IsNullOrWhiteSpace(urlAttribute.Value)
                                                         && Uri.IsWellFormedUriString(urlAttribute.Value,
                                                             UriKind.Absolute))
                                {
                                    embed.WithImageUrl(urlAttribute.Value);
                                    gotImage = true;
                                }
                            }
                        }


                        embed.WithTitle(title.TrimTo(256));

                        var desc = feedItem.Description?.StripHtml();
                        if (!string.IsNullOrWhiteSpace(feedItem.Description))
                            embed.WithDescription(desc.TrimTo(2048));
    
                        //send the created embed to all subscribed channels
                        var feedSendTasks = value.Where(x => x.GuildConfig != null);
                        foreach (var feed1 in feedSendTasks)
                        {
                            var channel = _client.GetGuild(feed1.GuildConfig.GuildId).GetTextChannel(feed1.ChannelId);
                            if (channel is null)
                                continue;
                            var (builder, content) = await GetFeedEmbed(repbuilder.Replace(feed1.Message));
                            if (feed1.Message is "-" or null)
                                allSendTasks.Add(channel.EmbedAsync(embed));
                            else
                                allSendTasks.Add(channel.SendMessageAsync(content ?? "", embed: builder?.Build()));
                        }

                    }
                }
                catch
                {
                    //ignored
                }
            }

            await Task.WhenAll(Task.WhenAll(allSendTasks), Task.Delay(10000)).ConfigureAwait(false);
        }
    }

    private Task<(EmbedBuilder builder, string content)> GetFeedEmbed(string message) 
        => SmartEmbed.TryParse(message, out var embed, out var content) ? Task.FromResult((embed, content)) : Task.FromResult<(EmbedBuilder, string)>((null, message));

    public List<FeedSub> GetFeeds(ulong guildId)
    {
        using var uow = _db.GetDbContext();
        return uow.ForGuildId(guildId,
                set => set.Include(x => x.FeedSubs))
            .FeedSubs
            .OrderBy(x => x.Id)
            .ToList();
    }

    public bool AddFeed(ulong guildId, ulong channelId, string rssFeed)
    {
        rssFeed.ThrowIfNull(nameof(rssFeed));

        var fs = new FeedSub
        {
            ChannelId = channelId,
            Url = rssFeed.Trim()
        };

        using var uow = _db.GetDbContext();
        var gc = uow.ForGuildId(guildId,
            set => set.Include(x => x.FeedSubs));

        if (gc.FeedSubs.Any(x => x.Url.ToLower() == fs.Url.ToLower()))
            return false;
        if (gc.FeedSubs.Count >= 10) return false;

        gc.FeedSubs.Add(fs);
        uow.SaveChanges();
        //adding all, in case bot wasn't on this guild when it started
        foreach (var feed in gc.FeedSubs)
            _subs.AddOrUpdate(feed.Url.ToLower(), new HashSet<FeedSub> {feed}, (_, old) =>
            {
                old.Add(feed);
                return old;
            });

        return true;
    }

    public async Task<bool> AddFeedMessage(ulong guildId, int index, string message)
    {
        if (index < 0)
            return false;
        await using var uow = _db.GetDbContext();
        var items = uow.ForGuildId(guildId, set => set.Include(x => x.FeedSubs))
                       .FeedSubs
                       .OrderBy(x => x.Id)
                       .ToList();
        var toupdate = items[index];
        _subs.AddOrUpdate(toupdate.Url.ToLower(), new HashSet<FeedSub>(), (_, old) =>
        {
            old.Remove(toupdate);
            return old;
        });
        toupdate.Message = message;
        uow.Update(toupdate);
        await uow.SaveChangesAsync();
        _subs.AddOrUpdate(toupdate.Url.ToLower(), new HashSet<FeedSub>(), (_, old) =>
        {
            old.Add(toupdate);
            return old;
        });
        return true;
    }
    public bool RemoveFeed(ulong guildId, int index)
    {
        if (index < 0)
            return false;

        using var uow = _db.GetDbContext();
        var items = uow.ForGuildId(guildId, set => set.Include(x => x.FeedSubs))
            .FeedSubs
            .OrderBy(x => x.Id)
            .ToList();

        if (items.Count <= index)
            return false;
        var toRemove = items[index];
        _subs.AddOrUpdate(toRemove.Url.ToLower(), new HashSet<FeedSub>(), (_, old) =>
        {
            old.Remove(toRemove);
            return old;
        });
        uow.Remove(toRemove);
        uow.SaveChanges();

        return true;
    }
}