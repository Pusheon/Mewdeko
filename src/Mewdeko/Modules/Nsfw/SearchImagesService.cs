using Newtonsoft.Json.Linq;
using Serilog;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Threading;
using Mewdeko.Common;
using Mewdeko._Extensions;
using Mewdeko.Database;
using Mewdeko.Database.Extensions;
using Mewdeko.Database.Models;
using Microsoft.EntityFrameworkCore;
using Booru = Mewdeko.Modules.Nsfw.Common.Booru;

namespace Mewdeko.Modules.Nsfw;

public record TagRequest(ulong GuildId, bool ForceExplicit, Booru SearchType, params string[] Tags);
public record UrlReply
{
    public string Error { get; init; }
    public string Url { get; init; }
    public string Rating { get; init; }
    public string Provider { get; init; }
    public List<string> Tags { get; } = new List<string>();
}

public class SearchImagesService : ISearchImagesService, INService
{
    private readonly Random _rng;
    private readonly HttpClient _http;
    private readonly SearchImageCacher _cache;
    private readonly IHttpClientFactory _httpFactory;
    private readonly DbService _db;
    private readonly ConcurrentDictionary<ulong, HashSet<string>> _blacklistedTags = new();

    public ConcurrentDictionary<ulong, Timer> AutoHentaiTimers { get; } = new();
    public ConcurrentDictionary<ulong, Timer> AutoBoobTimers { get; } = new();
    public ConcurrentDictionary<ulong, Timer> AutoButtTimers { get; } = new();

    public SearchImagesService(DbService db,
        IHttpClientFactory http,
        SearchImageCacher cacher,
        IHttpClientFactory httpFactory, Mewdeko bot)
    {
        _db = db;
        _rng = new MewdekoRandom();
        _http = http.CreateClient();
        _http.AddFakeHeaders();
        _cache = cacher;
        _httpFactory = httpFactory;

        _blacklistedTags = new ConcurrentDictionary<ulong, HashSet<string>>(
            bot.AllGuildConfigs.ToDictionary(
                x => x.GuildId,
                x => new HashSet<string>(x.NsfwBlacklistedTags.Select(y => y.Tag))));
    }

    private Task<UrlReply> GetNsfwImageAsync(ulong? guildId, bool forceExplicit, string[]? tags, Booru dapi, CancellationToken cancel = default) => GetNsfwImageAsync(guildId ?? 0, tags ?? Array.Empty<string>(), forceExplicit, dapi, cancel);

    private bool IsValidTag(string tag) => tag.All(x => x != '+' && x != '?' && x != '/'); // tags mustn't contain + or ? or /

    private async Task<UrlReply> GetNsfwImageAsync(
        ulong guildId,
        string[] tags,
        bool forceExplicit,
        Booru dapi,
        CancellationToken cancel)
    {
        if (!tags.All(x => IsValidTag(x)))
        {
            return new UrlReply
            {
                Error = "One or more tags are invalid.",
                Url = ""
            };
        }
#if  DEBUG
        Log.Information("Getting {V} image for Guild: {GuildId}...", dapi.ToString(), guildId);  
#endif
        try
        {
            _blacklistedTags.TryGetValue(guildId, out var blTags);

            switch (dapi)
            {
                case Booru.E621:
                    {
                        for (var i = 0; i < tags.Length; ++i)
                            if (tags[i] == "yuri")
                                tags[i] = "female/female";
                        break;
                    }
                case Booru.Derpibooru:
                    {
                        for (var i = 0; i < tags.Length; ++i)
                            if (tags[i] == "yuri")
                                tags[i] = "lesbian";
                        break;
                    }
            }

            var result = await _cache.GetImageNew(tags, forceExplicit, dapi, blTags ?? new HashSet<string>(), cancel)
                                     .ConfigureAwait(false);

            if (result is null)
            {
                return new UrlReply
                {
                    Error = "Image not found.",
                    Url = ""
                };
            }

            var reply = new UrlReply
            {
                Error = "",
                Url = result.FileUrl,
                Rating = result.Rating,
                Provider = result.SearchType.ToString()
            };

            reply.Tags.AddRange(result.Tags);

            return reply;

        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed getting {Dapi} image: {Message}", dapi, ex.Message);
            return new UrlReply
            {
                Error = ex.Message,
                Url = ""
            };
        }
    }

    public Task<UrlReply> Gelbooru(ulong? guildId, bool forceExplicit, string[] tags)
        => GetNsfwImageAsync(guildId, forceExplicit, tags, Booru.Gelbooru);

    public Task<UrlReply> Danbooru(ulong? guildId, bool forceExplicit, string[] tags)
        => GetNsfwImageAsync(guildId, forceExplicit, tags, Booru.Danbooru);

    public Task<UrlReply> Konachan(ulong? guildId, bool forceExplicit, string[] tags)
        => GetNsfwImageAsync(guildId, forceExplicit, tags, Booru.Konachan);

    public Task<UrlReply> Yandere(ulong? guildId, bool forceExplicit, string[] tags)
        => GetNsfwImageAsync(guildId, forceExplicit, tags, Booru.Yandere);

    public Task<UrlReply> Rule34(ulong? guildId, bool forceExplicit, string[] tags)
        => GetNsfwImageAsync(guildId, forceExplicit, tags, Booru.Rule34);

    public Task<UrlReply> E621(ulong? guildId, bool forceExplicit, string[] tags)
        => GetNsfwImageAsync(guildId, forceExplicit, tags, Booru.E621);

    public Task<UrlReply> DerpiBooru(ulong? guildId, bool forceExplicit, string[] tags)
        => GetNsfwImageAsync(guildId, forceExplicit, tags, Booru.Derpibooru);

    public Task<UrlReply> SafeBooru(ulong? guildId, bool forceExplicit, string[] tags)
        => GetNsfwImageAsync(guildId, forceExplicit, tags, Booru.Safebooru);
        
    public Task<UrlReply> Sankaku(ulong? guildId, bool forceExplicit, string[] tags)
        => GetNsfwImageAsync(guildId, forceExplicit, tags, Booru.Sankaku);

    public async Task<UrlReply> Hentai(ulong? guildId, bool forceExplicit, string[] tags)
    {
        var providers = new[] {
            Booru.Danbooru,
            Booru.Konachan,
            Booru.Gelbooru,
            Booru.Yandere
        };

        using var cancelSource = new CancellationTokenSource();

        // create a task for each type
        var tasks = providers.Select(type => GetNsfwImageAsync(guildId, forceExplicit, tags, type)).ToList();
        do
        {
            // wait for any of the tasks to complete
            var task = await Task.WhenAny(tasks);

            // get its result
            var result = task.GetAwaiter().GetResult();
            if(result.Error == "")
            {
                // if we have a non-error result, cancel other searches and return the result
                cancelSource.Cancel();
                return result;
            }

            // if the result is an error, remove that task from the waiting list,
            // and wait for another task to complete
            tasks.Remove(task);
        }
        while (tasks.Count > 0); // keep looping as long as there is any task remaining to be attempted

        // if we ran out of tasks, that means all tasks failed - return an error
        return new UrlReply()
        {
            Error = "No hentai image found."
        };
    }

    public async Task<UrlReply> Boobs()
    {
        try
        {
            JToken obj;
            obj = JArray.Parse(await _http.GetStringAsync($"http://api.oboobs.ru/boobs/{_rng.Next(0, 12000)}").ConfigureAwait(false))[0];
            return new UrlReply
            {
                Error = "",
                Url = $"http://media.oboobs.ru/{obj["preview"]}",
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error retreiving boob image: {Message}", ex.Message);
            return new UrlReply
            {
                Error = ex.Message,
                Url = "",
            };
        }
    }

    private readonly object taglock = new();
    public ValueTask<bool> ToggleBlacklistTag(ulong guildId, string tag)
    {
        var tagObj = new NsfwBlacklitedTag
        {
            Tag = tag
        };

        bool added;
        using var uow = _db.GetDbContext();
        var gc = uow.ForGuildId(guildId, set => set.Include(y => y.NsfwBlacklistedTags));
        if (gc.NsfwBlacklistedTags.Add(tagObj))
        {
            added = true;
        }
        else
        {
            gc.NsfwBlacklistedTags.Remove(tagObj);
            var toRemove = gc.NsfwBlacklistedTags.FirstOrDefault(x => x.Equals(tagObj));
            if (toRemove != null)
                uow.Remove(toRemove);
            added = false;
        }

        var newTags = new HashSet<string>(gc.NsfwBlacklistedTags.Select(x => x.Tag));
        _blacklistedTags.AddOrUpdate(guildId, newTags, delegate { return newTags; });

        uow.SaveChanges();

        return new ValueTask<bool>(added);
    }

    public ValueTask<string[]> GetBlacklistedTags(ulong guildId)
    {
        lock (taglock)
        {
            return _blacklistedTags.TryGetValue(guildId, out var tags) ? new ValueTask<string[]>(tags.ToArray()) : new ValueTask<string[]>(Array.Empty<string>());
        }
    }

    public async Task<UrlReply> Butts()
    {
        try
        {
            JToken obj;
            obj = JArray.Parse(await _http.GetStringAsync($"http://api.obutts.ru/butts/{_rng.Next(0, 6100)}"))[0];
            return new UrlReply
            {
                Error = "",
                Url = $"http://media.obutts.ru/{obj["preview"]}",
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error retreiving butt image: {Message}", ex.Message);
            return new UrlReply
            {
                Error = ex.Message,
                Url = "",
            };
        }
    }
}