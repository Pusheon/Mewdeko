using Discord;
using Discord.Commands;
using Mewdeko.Common;
using Mewdeko.Common.Attributes;
using Mewdeko.Common.Collections;
using Mewdeko._Extensions;
using Newtonsoft.Json.Linq;
using System.Net.Http;
using System.Threading;
using Fergun.Interactive;
using Fergun.Interactive.Pagination;
using MartineApiNet;
using MartineApiNet.Enums;
using NHentai.NET.Client;
using NHentai.NET.Models.Searches;
using Refit;

namespace Mewdeko.Modules.Nsfw;

public class Nsfw : MewdekoModuleBase<ISearchImagesService>
{
    private static readonly ConcurrentHashSet<ulong> _hentaiBombBlacklist = new();
    private readonly IHttpClientFactory _httpFactory;
    private readonly MewdekoRandom _rng;
    private readonly InteractiveService _interactivity;
    private readonly MartineApi _martineApi;
    public static List<RedditCache> Cache { get; set; } = new();

    public Nsfw(IHttpClientFactory factory, InteractiveService interactivity, MartineApi martineApi)
    {
        _martineApi = martineApi;
        _interactivity = interactivity;
        _httpFactory = factory;
        _rng = new MewdekoRandom();
    }

    public record RedditCache
    {
        public IGuild Guild { get; set; }
        public string Url { get; set; }
    }

    public static bool CheckIfAlreadyPosted(IGuild guild, string url)
    {
        var e = new RedditCache { Guild = guild, Url = url };
        if (!Cache.Any())
        {
            Cache.Add(e);
            return false;
        }

        if (Cache.Contains(e)) return Cache.Contains(e) || true;
        Cache.Add(e);
        return false;

    }

    private async Task InternalBoobs()
    {
        try
        {
            JToken obj;
            using (var http = _httpFactory.CreateClient())
            {
                obj = JArray.Parse(await http
                                         .GetStringAsync(
                                             $"http://api.oboobs.ru/boobs/{new MewdekoRandom().Next(0, 10330)}")
                                         .ConfigureAwait(false))[0];
            }

            await ctx.Channel.SendMessageAsync($"http://media.oboobs.ru/{obj["preview"]}").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await ctx.Channel.SendErrorAsync(ex.Message).ConfigureAwait(false);
        }
    }

    private async Task InternalButts(IMessageChannel Channel)
    {
        try
        {
            JToken obj;
            using (var http = _httpFactory.CreateClient())
            {
                obj = JArray.Parse(await http
                                         .GetStringAsync(
                                             $"http://api.obutts.ru/butts/{new MewdekoRandom().Next(0, 4335)}")
                                         .ConfigureAwait(false))[0];
            }

            await Channel.SendMessageAsync($"http://media.obutts.ru/{obj["preview"]}").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await ctx.Channel.SendErrorAsync(ex.Message).ConfigureAwait(false);
        }
    }

    [MewdekoCommand, Usage, Description, Alias, RequireContext(ContextType.Guild), RequireNsfw]
    public async Task RedditNsfw(string subreddit)
    {
        try
        {
            var image = await _martineApi.RedditApi.GetRandomFromSubreddit(subreddit, Toptype.year);
            while (CheckIfAlreadyPosted(ctx.Guild, image.Data.ImageUrl))
                image = await _martineApi.RedditApi.GetRandomFromSubreddit(subreddit, Toptype.year);
            var eb = new EmbedBuilder
            {
                Description = $"[{image.Data.Title}]({image.Data.PostUrl})",
                ImageUrl = image.Data.ImageUrl,
                Color = Mewdeko.OkColor
            };
            await ctx.Channel.SendMessageAsync("", embed: eb.Build());
        }
        catch (ApiException)
        {
            await ctx.Channel.SendErrorAsync(
                $"Hey guys stop spamming the command! The api can only take so much man. Wait at least a few mins before trying again. If theres an issue join the support sevrer in {Prefix}vote.");
        }
    }

    [MewdekoCommand, Usage, Description, Alias, RequireContext(ContextType.Guild), RequireNsfw]
    public async Task NHentai(int num)
    {
        var client = new HentaiClient();
        var book = await client.SearchBookAsync(num);
        var title = book.Titles.English;
        var pages = book.GetPages();
        var tags = new List<string>();
        foreach (var i in book.Tags) tags.Add(i.Name);
        if (tags.Contains("lolicon") || tags.Contains("loli") || tags.Contains("shotacon") || tags.Contains("shota"))

        {
            await ctx.Channel.SendErrorAsync("This manga contains loli/shota content and is not allowed by discord TOS!");
            return;
        }

        var paginator = new LazyPaginatorBuilder()
            .AddUser(ctx.User)
            .WithPageFactory(PageFactory)
            .WithFooter(PaginatorFooter.PageNumber | PaginatorFooter.Users)
            .WithMaxPageIndex(pages.Count())
            .WithDefaultEmotes()
            .Build();

        await _interactivity.SendPaginatorAsync(paginator, Context.Channel, TimeSpan.FromMinutes(60));

        async Task<PageBuilder> PageFactory(int page)
        {
            await Task.CompletedTask;
            var enumerable = pages as string[] ?? pages.ToArray();
            return new PageBuilder()
                .WithTitle($"{Format.Bold($"{title}")} - {enumerable.ToArray().Length} pages")
                .WithImageUrl(pages.Skip(page).FirstOrDefault())
                .WithColor((Color) System.Drawing.Color.FromArgb(page * 1500));
        }
    }

    public async Task InternalNHentaiSearch(string search, int page = 1, string type = "popular",
        string? exclude = null)
    {
        var client = new HentaiClient();
        var e = type.ToLower() switch
        {
            "date" => Sort.Date,
            "popular" => Sort.Popular,
            _ => Sort.Date
        };

        var result = await client.SearchQueryAsync(page, e, search, $"{exclude} -lolicon -loli");
        if (!result.Books.Any())
        {
            await ctx.Channel.SendErrorAsync("The search returned no results. Try again with a different query!");
            return;
        }

        var paginator = new LazyPaginatorBuilder()
            .AddUser(ctx.User)
            .WithPageFactory(PageFactory)
            .WithFooter(PaginatorFooter.PageNumber | PaginatorFooter.Users)
            .WithMaxPageIndex(result.Books.Count - 1)
            .WithDefaultEmotes()
            .Build();

        await _interactivity.SendPaginatorAsync(paginator, Context.Channel, TimeSpan.FromMinutes(60));

        async Task<PageBuilder> PageFactory(int page)
        {
            await Task.CompletedTask;
            var list = result.Books.Skip(page).FirstOrDefault().Tags.Select(i => $"[{i.Name}](https://nhentai.net{i.Url})").ToList();
            return new PageBuilder().WithOkColor()
                                    .WithTitle(result.Books.Skip(page).FirstOrDefault().Titles.English)
                                    .WithDescription(string.Join("|", list.Take(20)))
                                    .AddField("NHentai Magic Number", result.Books.Skip(page).FirstOrDefault().Id)
                                    .AddField("NHentai Magic URL",
                                        $"https://nhentai.net/g/{result.Books.Skip(page).FirstOrDefault().Id}")
                                    .AddField("Pages", result.Books.Skip(page).FirstOrDefault().PagesCount)
                                    .WithImageUrl(result.Books.Skip(page).FirstOrDefault().GetCover());
        }
    }

    [MewdekoCommand, Usage, Description, Alias, RequireContext(ContextType.Guild), RequireNsfw]
    public async Task NHentaiSearch([Remainder] string search) => await InternalNHentaiSearch(search);

    [MewdekoCommand, Usage, Description, Alias, RequireContext(ContextType.Guild), RequireNsfw]
    public async Task NHentaiSearch(string search, [Remainder] string blacklist) => await InternalNHentaiSearch(search, 1, blacklist);

    [MewdekoCommand, Usage, Description, Alias, RequireContext(ContextType.Guild), RequireNsfw]
    public async Task NHentaiSearch(string search, int page) => await InternalNHentaiSearch(search, page);

    [MewdekoCommand, Usage, Description, Alias, RequireContext(ContextType.Guild), RequireNsfw]
    public async Task NHentaiSearch(string search, int page, string type) => await InternalNHentaiSearch(search, page, type);

    [MewdekoCommand, Usage, Description, Alias, RequireContext(ContextType.Guild), RequireNsfw]
    public async Task NHentaiSearch(string search, int page, string type, [Remainder] string blacklist) => await InternalNHentaiSearch(search, page, type, blacklist);
    [MewdekoCommand, Aliases]
    [RequireNsfw]
    [RequireContext(ContextType.Guild)]
    [UserPerm(ChannelPermission.ManageMessages)]
    public async Task AutoHentai(int interval = 0, [Remainder] string? tags = null)
    {
        Timer t = default;

        switch (interval)
        {
            case 0 when !Service.AutoHentaiTimers.TryRemove(ctx.Channel.Id, out t):
                return;
            case 0:
                t.Change(Timeout.Infinite, Timeout.Infinite); //proper way to disable the timer
                await ReplyConfirmLocalizedAsync("stopped").ConfigureAwait(false);
                return;
            case < 20:
                return;
        }

        t = new Timer(async (state) =>
        {
            try
            {
                if (tags is null || tags.Length == 0)
                    await InternalDapiCommand(null, true, Service.Hentai).ConfigureAwait(false);
                else
                {
                    var groups = tags.Split('|');
                    var group = groups[_rng.Next(0, groups.Length)];
                    await InternalDapiCommand(group.Split(' '), true, Service.Hentai).ConfigureAwait(false);
                }
            }
            catch
            {
                // ignored
            }
        }, null, interval * 1000, interval * 1000);

        Service.AutoHentaiTimers.AddOrUpdate(ctx.Channel.Id, t, (key, old) =>
        {
            old.Change(Timeout.Infinite, Timeout.Infinite);
            return t;
        });

        await ReplyConfirmLocalizedAsync("autohentai_started",
            interval,
            string.Join(", ", tags));
    }

    [MewdekoCommand, Aliases]
    [RequireNsfw]
    [RequireContext(ContextType.Guild)]
    [UserPerm(ChannelPermission.ManageMessages)]
    public async Task AutoBoobs(int interval = 0)
    {
        Timer t;

        if (interval == 0)
        {
            if (!Service.AutoHentaiTimers.TryRemove(ctx.Channel.Id, out t)) return;

            t.Change(Timeout.Infinite, Timeout.Infinite); //proper way to disable the timer
            await ReplyConfirmLocalizedAsync("stopped").ConfigureAwait(false);
            return;
        }

        t = new Timer(async (state) =>
        {
            try
            {
                await InternalBoobs().ConfigureAwait(false);
            }
            catch
            {
                // ignored
            }
        }, null, interval * 1000, interval * 1000);

        Service.AutoBoobTimers.AddOrUpdate(ctx.Channel.Id, t, (key, old) =>
        {
            old.Change(Timeout.Infinite, Timeout.Infinite);
            return t;
        });

        await ReplyConfirmLocalizedAsync("started", interval);
    }

    [MewdekoCommand, Aliases]
    [RequireNsfw(Group = "nsfw_or_dm"), RequireContext(ContextType.DM, Group = "nsfw_or_dm")]
    [UserPerm(ChannelPermission.ManageMessages)]
    public async Task AutoButts(int interval = 0)
    {
        Timer t = default;

        switch (interval)
        {
            case 0 when !Service.AutoButtTimers.TryRemove(ctx.Channel.Id, out t):
                return;
            case 0:
                t.Change(Timeout.Infinite, Timeout.Infinite); //proper way to disable the timer
                await ReplyConfirmLocalizedAsync("stopped").ConfigureAwait(false);
                return;
            case < 20:
                return;
        }

        t = new Timer(async (state) =>
        {
            try
            {
                await InternalButts(ctx.Channel).ConfigureAwait(false);
            }
            catch
            {
                // ignored
            }
        }, null, interval * 1000, interval * 1000);

        Service.AutoButtTimers.AddOrUpdate(ctx.Channel.Id, t, (key, old) =>
        {
            old.Change(Timeout.Infinite, Timeout.Infinite);
            return t;
        });

        await ReplyConfirmLocalizedAsync("started", interval);
    }

    [MewdekoCommand, Aliases]
    [RequireNsfw(Group = "nsfw_or_dm"), RequireContext(ContextType.DM, Group = "nsfw_or_dm")]
    public Task Hentai(params string[] tags) 
        => InternalDapiCommand(tags, true, Service.Hentai);

    [MewdekoCommand, Aliases]
    [RequireNsfw(Group = "nsfw_or_dm"), RequireContext(ContextType.DM, Group = "nsfw_or_dm")]
    public async Task HentaiBomb(params string[] tags)
    {
        if (!_hentaiBombBlacklist.Add(ctx.Guild?.Id ?? ctx.User.Id))
            return;
        try
        {
            var images = await Task.WhenAll(Service.Yandere(ctx.Guild?.Id, true, tags),
                Service.Danbooru(ctx.Guild?.Id, true, tags),
                Service.Konachan(ctx.Guild?.Id, true, tags),
                Service.Gelbooru(ctx.Guild?.Id, true, tags));

            var linksEnum = images?.Where(l => l != null).ToArray();
            if (images is null || !linksEnum.Any())
            {
                await ReplyErrorLocalizedAsync("no_results").ConfigureAwait(false);
                return;
            }

            await ctx.Channel.SendMessageAsync(string.Join("\n\n", linksEnum.Select(x => x.Url)))
                     .ConfigureAwait(false);
        }
        finally
        {
            _hentaiBombBlacklist.TryRemove(ctx.Guild?.Id ?? ctx.User.Id);
        }
    }

    [MewdekoCommand, Aliases]
    [RequireNsfw(Group = "nsfw_or_dm"), RequireContext(ContextType.DM, Group = "nsfw_or_dm")]
    public Task Yandere(params string[] tags)
        => InternalDapiCommand(tags, false, Service.Yandere);

    [MewdekoCommand, Aliases]
    [RequireNsfw(Group = "nsfw_or_dm"), RequireContext(ContextType.DM, Group = "nsfw_or_dm")]
    public Task Konachan(params string[] tags)
        => InternalDapiCommand(tags, false, Service.Konachan);

    [MewdekoCommand, Aliases]
    [RequireNsfw(Group = "nsfw_or_dm"), RequireContext(ContextType.DM, Group = "nsfw_or_dm")]
    public Task Sankaku(params string[] tags)
        => InternalDapiCommand(tags, false, Service.Sankaku);

    [MewdekoCommand, Aliases]
    [RequireNsfw(Group = "nsfw_or_dm"), RequireContext(ContextType.DM, Group = "nsfw_or_dm")]
    public Task E621(params string[] tags)
        => InternalDapiCommand(tags, false, Service.E621);

    [MewdekoCommand, Aliases]
    [RequireNsfw(Group = "nsfw_or_dm"), RequireContext(ContextType.DM, Group = "nsfw_or_dm")]
    public Task Rule34(params string[] tags)
        => InternalDapiCommand(tags, false, Service.Rule34);

    [MewdekoCommand, Aliases]
    [RequireNsfw(Group = "nsfw_or_dm"), RequireContext(ContextType.DM, Group = "nsfw_or_dm")]
    public Task Danbooru(params string[] tags)
        => InternalDapiCommand(tags, false, Service.Danbooru);

    [MewdekoCommand, Aliases]
    [RequireNsfw(Group = "nsfw_or_dm"), RequireContext(ContextType.DM, Group = "nsfw_or_dm")]
    public Task Gelbooru(params string[] tags)
        => InternalDapiCommand(tags, false, Service.Gelbooru);

    [MewdekoCommand, Aliases]
    [RequireNsfw(Group = "nsfw_or_dm"), RequireContext(ContextType.DM, Group = "nsfw_or_dm")]
    public Task Derpibooru(params string[] tags)
        => InternalDapiCommand(tags, false, Service.DerpiBooru);

    [MewdekoCommand, Aliases]
    [RequireNsfw(Group = "nsfw_or_dm"), RequireContext(ContextType.DM, Group = "nsfw_or_dm")]
    public Task Safebooru(params string[] tags)
        => InternalDapiCommand(tags, false, Service.SafeBooru);

    [MewdekoCommand, Aliases]
    [RequireNsfw(Group = "nsfw_or_dm"), RequireContext(ContextType.DM, Group = "nsfw_or_dm")]
    public async Task Boobs()
    {
        try
        {
            JToken obj;
            using (var http = _httpFactory.CreateClient())
            {
                obj = JArray.Parse(await http
                                         .GetStringAsync($"http://api.oboobs.ru/boobs/{new MewdekoRandom().Next(0, 12000)}")
                                         .ConfigureAwait(false))[0];
            }

            await ctx.Channel.SendMessageAsync($"http://media.oboobs.ru/{obj["preview"]}").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await ctx.Channel.SendErrorAsync(ex.Message).ConfigureAwait(false);
        }
    }

    [MewdekoCommand, Aliases]
    [RequireNsfw(Group = "nsfw_or_dm"), RequireContext(ContextType.DM, Group = "nsfw_or_dm")]
    public async Task Butts()
    {
        try
        {
            JToken obj;
            using (var http = _httpFactory.CreateClient())
            {
                obj = JArray.Parse(await http
                                         .GetStringAsync($"http://api.obutts.ru/butts/{new MewdekoRandom().Next(0, 6100)}")
                                         .ConfigureAwait(false))[0];
            }

            await ctx.Channel.SendMessageAsync($"http://media.obutts.ru/{obj["preview"]}").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await ctx.Channel.SendErrorAsync(ex.Message).ConfigureAwait(false);
        }
    }

    [MewdekoCommand, Aliases]
    [RequireContext(ContextType.Guild)]
    [UserPerm(GuildPermission.ManageMessages)]
    public async Task NsfwTagBlacklist([Remainder] string? tag = null)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            var blTags = await Service.GetBlacklistedTags(ctx.Guild.Id);
            await ctx.Channel.SendConfirmAsync(GetText("blacklisted_tag_list"),
                blTags.Any()
                    ? string.Join(", ", blTags)
                    : "-").ConfigureAwait(false);
        }
        else
        {
            tag = tag.Trim().ToLowerInvariant();
            var added = await Service.ToggleBlacklistTag(ctx.Guild.Id, tag);

            if (added)
                await ReplyConfirmLocalizedAsync("blacklisted_tag_add", tag);
            else
                await ReplyConfirmLocalizedAsync("blacklisted_tag_remove", tag);
        }
    }

    private async Task InternalDapiCommand(string[] tags,
        bool forceExplicit,
        Func<ulong?, bool, string[], Task<UrlReply>> func)
    {
        var data = await func(ctx.Guild?.Id, forceExplicit, tags);
            
        if (data is null || !string.IsNullOrWhiteSpace(data.Error))
        {
            await ReplyErrorLocalizedAsync("no_results");
            return;
        }

        await ctx.Channel.SendMessageAsync(embed: new EmbedBuilder().WithOkColor().WithImageUrl(data.Url)
                                                                    .WithDescription($"[link]({data.Url})")
                                                                    .WithFooter(
                                                                        $"{data.Rating} ({data.Provider}) | {string.Join(" | ", data.Tags.Where(x => !string.IsNullOrWhiteSpace(x)).Take(5))}")
                                                                    .Build());
    }
}