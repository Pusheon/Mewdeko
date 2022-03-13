#nullable enable
using System.IO;
using System.Net.Http;
using Anilist4Net;
using Anilist4Net.Enums;
using Discord;
using Discord.Commands;
using Fergun.Interactive;
using Fergun.Interactive.Pagination;
using JikanDotNet;
using NekosBestApiNet;
using Mewdeko._Extensions;
using MartineApiNet;
using Mewdeko.Common;
using Mewdeko.Common.Attributes;
using Mewdeko.Database.Extensions;
using Newtonsoft.Json;

namespace Mewdeko.Modules.Searches;

public partial class Searches
{
    [Group]
    public class AnimeCommands : MewdekoSubmodule
    {
        public readonly NekosBestApi NekosBestApi;
        private readonly MartineApi _martineApi;
        private readonly InteractiveService _interactivity;

        public AnimeCommands(InteractiveService service, MartineApi martineApi, NekosBestApi nekosBestApi)
        {
            _interactivity = service;
            _martineApi = martineApi;
            NekosBestApi = nekosBestApi;
        }

        [MewdekoCommand, Usage, Description, Aliases]
        public async Task Ship(IUser user, IUser user2)
        {
            var random = new Random().Next(0, 100);
            var shipRequest = await _martineApi.ImageGenerationApi.GenerateShipImage(random, user.RealAvatarUrl().AbsoluteUri, user2.RealAvatarUrl().AbsoluteUri);
            var bytes = await shipRequest.ReadAsByteArrayAsync();
            await using var ms = new MemoryStream(bytes);
            var color = new Color();
            var response = string.Empty;
            switch (random)
            {
                case < 30:
                    response = "No chance, just none. Don't even think about it.";
                    color = Discord.Color.Red;
                    break;
                case <= 50 and >= 31:
                    response = "You may have a chance but don't try too hard.";
                    color = Discord.Color.Teal;
                    break;
                case 69:
                    response = "Go 69 that mfer";
                    color = Discord.Color.DarkRed;
                    break;
                case <=70 and >= 60:
                    response = "I mean, go for it, I guess, looks like you would do good";
                    color = Discord.Color.Magenta;
                    break;
                case <=100 and >= 71:
                    response =
                        "Horoscopes conclude that today will be a good day.. And that you two will get a room together soon";
                    color = Discord.Color.Red;
                    break;

            }
            await ctx.Channel.SendFileAsync(ms, "ship.png", embed: new EmbedBuilder().WithColor(color).WithDescription($"You are {random}% compatible. {response}").WithImageUrl("attachment://ship.png").Build());
        }

        [MewdekoCommand, Usage, Description, Aliases]
        public async Task Ship(IUser user)
        {
            var random = new Random().Next(0, 100);
            var shipRequest = await _martineApi.ImageGenerationApi.GenerateShipImage(random, user.RealAvatarUrl().AbsoluteUri, ctx.User.RealAvatarUrl().AbsoluteUri);
            var bytes = await shipRequest.ReadAsByteArrayAsync();
            await using var ms = new MemoryStream(bytes);
            var color = new Color();
            var response = string.Empty;
            switch (random)
            {
                case < 30:
                    response = "No chance, just none. Don't even think about it.";
                    color = Discord.Color.Red;
                    break;
                case <= 50 and >= 31:
                    response = "You may have a chance but don't try too hard.";
                    color = Discord.Color.Teal;
                    break;
                case 69:
                    response = "Go 69 that mfer";
                    color = Discord.Color.DarkRed;
                    break;
                case <=70 and >= 60:
                    response = "I mean, go for it, I guess, looks like you would do good";
                    color = Discord.Color.Magenta;
                    break;
                case <=100 and >= 71:
                    response =
                        "Horoscopes conclude that today will be a good day.. And that you two will get a room together soon";
                    color = Discord.Color.Red;
                    break;

            }
            await ctx.Channel.SendFileAsync(ms, "ship.png", embed: new EmbedBuilder().WithColor(color).WithDescription($"You are {random}% compatible. {response}").WithImageUrl("attachment://ship.png").Build());
        }

        [MewdekoCommand, Usage, Description]
        public async Task Hug(IUser user)
        {
            var req = await NekosBestApi.ActionsApi.Hug();
            var em = new EmbedBuilder
            {
                Description = $"{ctx.User.Mention} hugged {user.Mention}",
                ImageUrl = req.Results.FirstOrDefault().Url,
                Color = Mewdeko.OkColor
            };
            await ctx.Channel.SendMessageAsync(embed: em.Build());
        }


        [MewdekoCommand, Usage, Description, Aliases]
        public async Task Kiss(IUser user)
        {
            var req = await NekosBestApi.ActionsApi.Kiss();
            var em = new EmbedBuilder
            {
                Description = $"{ctx.User.Mention} kissed {user.Mention}",
                ImageUrl = req.Results.FirstOrDefault().Url,
                Color = Mewdeko.OkColor
            };
            await ctx.Channel.SendMessageAsync("", embed: em.Build());
        }

        [MewdekoCommand, Usage, Description, Aliases]
        public async Task Pat(IUser user)
        {
            var req = await NekosBestApi.ActionsApi.Pat();
            var em = new EmbedBuilder
            {
                Description = $"{ctx.User.Mention} gave pattus to {user.Mention}",
                ImageUrl = req.Results.FirstOrDefault().Url,
                Color = Mewdeko.OkColor
            };
            await ctx.Channel.SendMessageAsync("", embed: em.Build());
        }

        [MewdekoCommand, Usage, Description, Aliases]
        public async Task Tickle(IUser user)
        {
            var req = await NekosBestApi.ActionsApi.Tickle();
            var em = new EmbedBuilder
            {
                Description = $"{ctx.User.Mention} tickled {user.Mention}",
                ImageUrl = req.Results.FirstOrDefault().Url,
                Color = Mewdeko.OkColor
            };
            await ctx.Channel.SendMessageAsync("", embed: em.Build());
        }

        [MewdekoCommand, Usage, Description, Aliases]
        public async Task Slap(IUser user)
        {
            var req = await NekosBestApi.ActionsApi.Slap();
            var em = new EmbedBuilder
            {
                Description = $"{ctx.User.Mention} slapped {user.Mention}",
                ImageUrl = req.Results.FirstOrDefault().Url,
                Color = Mewdeko.OkColor
            };
            await ctx.Channel.SendMessageAsync("", embed: em.Build());
        }

        [MewdekoCommand, Usage, Description, Aliases]
        public async Task Cuddle(IUser user)
        {
            var req = await NekosBestApi.ActionsApi.Cuddle();
            var em = new EmbedBuilder
            {
                Description = $"{ctx.User.Mention} cuddled with {user.Mention}",
                ImageUrl = req.Results.FirstOrDefault().Url,
                Color = Mewdeko.OkColor
            };
            await ctx.Channel.SendMessageAsync("", embed: em.Build());
        }

        [MewdekoCommand, Usage, Description, Aliases]
        public async Task Poke(IUser user)
        {
            var req = await NekosBestApi.ActionsApi.Poke();
            var em = new EmbedBuilder
            {
                Description = $"{ctx.User.Mention} poked {user.Mention}",
                ImageUrl = req.Results.FirstOrDefault().Url,
                Color = Mewdeko.OkColor
            };
            await ctx.Channel.SendMessageAsync("", embed: em.Build());
        }

        [MewdekoCommand, Usage, Description, Aliases]
        public async Task Feed(IUser user)
        {
            var req = await NekosBestApi.ActionsApi.Feed();
            var em = new EmbedBuilder
            {
                Description = $"{ctx.User.Mention} fed {user.Mention}",
                ImageUrl = req.Results.FirstOrDefault().Url,
                Color = Mewdeko.OkColor
            };
            await ctx.Channel.SendMessageAsync("", embed: em.Build());
        }

        [MewdekoCommand, Usage, Description, Aliases]
        public async Task RandomNeko()
        {
            var req = await NekosBestApi.CategoryApi.Neko();
            var em = new EmbedBuilder
            {
                Description = $"nya~ [Source]({req.Results.FirstOrDefault().SourceUrl})",
                ImageUrl = req.Results.FirstOrDefault().Url,
                Color = Mewdeko.OkColor
            };
            await ctx.Channel.SendMessageAsync("", embed: em.Build());
        }
        
        [MewdekoCommand, Usage, Description, Aliases]
        public async Task RandomKitsune()
        {
            var req = await NekosBestApi.CategoryApi.Kitsune();
            var em = new EmbedBuilder
            {
                Description = $"What does the fox say? [Source]({req.Results.FirstOrDefault().SourceUrl})",
                ImageUrl = req.Results.FirstOrDefault().Url,
                Color = Mewdeko.OkColor
            };
            await ctx.Channel.SendMessageAsync("", embed: em.Build());
        }

        [MewdekoCommand, Usage, Description, Aliases]
        public async Task Shoot(IUser user)
        {
            var shootarray = new List<string>
            {
                "https://media.tenor.com/images/05085e9bc817361e783ad92a248ef318/tenor.gif",
                "https://media1.tenor.com/images/a0caaaec7f3f48fbcf037dd9e6a89c51/tenor.gif?itemid=12545029",
                "https://i.gifer.com/nin.gif",
                "https://i.imgflip.com/4fq6gm.gif",
                "https://cdn.myanimelist.net/s/common/uploaded_files/1448410154-7ba874393492485cf61797451b67a3be.gif",
                "https://thumbs.gfycat.com/DisguisedSimpleAmmonite-size_restricted.gif",
                "https://media0.giphy.com/media/a5OCMAro7MGQg/giphy.gif",
                "https://media1.tenor.com/images/e9f33b7ded139a73590878cf3f9d59a4/tenor.gif?itemid=16999058",
                "http://i.imgur.com/ygeo65P.gif",
                "https://gifimage.net/wp-content/uploads/2017/09/anime-shooting-gif-4.gif",
                "https://media0.giphy.com/media/rq8vsqrQmB128/giphy.gif",
                "https://pa1.narvii.com/6122/e688de863dc18f51f56cd5aabc677f7371a83701_hq.gif",
                "https://i2.wp.com/i.pinimg.com/originals/22/bb/ad/22bbade48e2ffa2c50968c635445b6a1.gif"
            };
            var rand = new Random();
            var index = rand.Next(shootarray.Count);
            var em = new EmbedBuilder
            {
                Description = $"{ctx.User.Mention} shot {user.Mention}",
                ImageUrl = shootarray[index],
                Color = Mewdeko.ErrorColor
            };
            await ctx.Channel.SendMessageAsync("", embed: em.Build());
        }

        [MewdekoCommand, Usage, Description, Aliases]
        public async Task FindAnime(string? e = null)
        {
            var t = string.Empty;
            if (e != null) t = e;
            if (e is null)
                try
                {
                    t = ctx.Message.Attachments.FirstOrDefault()?.Url;
                }
                catch
                {
                    await ctx.Channel.SendErrorAsync("You need to attach a file or use a url with this!");
                    return;
                }

            var c2 = new Client();
            var client = new HttpClient();
            var response = await client.PostAsync(
                $"https://api.trace.moe/search?url={t}", null);
            var responseContent = response.Content;
            using var reader = new StreamReader(await responseContent.ReadAsStreamAsync());
            var er = await reader.ReadToEndAsync();
            var stuff = JsonConvert.DeserializeObject<MoeResponse>(er,
                new JsonSerializerSettings {NullValueHandling = NullValueHandling.Ignore});
            var ert = stuff?.MoeResults.FirstOrDefault();
            if (ert?.Filename is null)
                await ctx.Channel.SendErrorAsync(
                    "No results found. Please try a different image, or avoid cropping the current one.");
            var image = await c2.GetMediaById(ert!.Anilist);
            var eb = new EmbedBuilder
            {
                ImageUrl = image?.CoverImageLarge,
                Color = Mewdeko.OkColor
            };
            var te = string.Empty;
            te = image?.SeasonInt.ToString()?[2..] is "" ? image.SeasonInt.ToString()?[1..] : image?.SeasonInt.ToString()?[2..];
            var entitle = image?.EnglishTitle;
            if (image?.EnglishTitle == null) entitle = "None";
            eb.AddField("English Title", entitle);
            eb.AddField("Japanese Title", image?.NativeTitle);
            eb.AddField("Romanji Title", image?.RomajiTitle);
            eb.AddField("Air Start Date", image?.AiringStartDate);
            eb.AddField("Air End Date", image?.AiringEndDate);
            eb.AddField("Season Number", te);
            if (ert.Episode is not null) eb.AddField("Episode", ert.Episode);
            eb.AddField("AniList Link", image?.SiteUrl);
            eb.AddField("MAL Link", $"https://myanimelist.net/anime/{image?.IdMal}");
            eb.AddField("Score", image?.MeanScore);
            eb.AddField("Description", image?.DescriptionMd.TrimTo(1024));
            _ = await ctx.Channel.SendMessageAsync("", embed: eb.Build());
        }

        [MewdekoCommand, Usage, Description, Aliases]
        public async Task CharInfo([Remainder] string chara)
        {
            var anilist = new Client();
            var te = await anilist.GetCharacterBySearch(chara);
            var desc = string.Empty;
            if (te.DescriptionMd is null) desc = "None";
            if (te.DescriptionMd != null) desc = te.DescriptionMd;
            if (te.DescriptionMd != null && te.DescriptionMd.Length > 1024) desc = te.DescriptionMd.TrimTo(1024);
            string altnames;
            if (te.AlternativeNames.FirstOrDefault() == "")
                altnames = "None";
            else
                altnames = string.Join(",", te.AlternativeNames);
            var eb = new EmbedBuilder();
            eb.AddField(" Full Name", te.FullName);
            eb.AddField("Alternative Names", altnames);
            eb.AddField("Native Name", te.NativeName);
            eb.AddField("Description/Backstory", desc);
            eb.ImageUrl = te.ImageLarge;
            eb.Color = Mewdeko.OkColor;
            await ctx.Channel.SendMessageAsync(embed: eb.Build());
        }


        [MewdekoCommand, Usage, Description, Aliases]
        public async Task Anime([Remainder] string query)
        {
            var c2 = new Client();
            Media? result;

            try
            {
                result = await c2.GetMediaBySearch(query, MediaTypes.ANIME);
            }
            catch (NullReferenceException)
            {
                await ctx.Channel.SendErrorAsync(
                    "The anime you searched for wasn't found! Please try a different query!");
                return;
            }

            var eb = new EmbedBuilder
            {
                ImageUrl = result?.CoverImageLarge
            };
            var list = new List<string?>();
            if (result != null && result.Recommendations.Nodes.Any())
                // ReSharper disable once AsyncVoidLambda
                result.Recommendations.Nodes.ForEach(async x =>
                {
                    if ((await c2.GetMediaById(x.Id)) is not null)
                        list.Add((await c2.GetMediaById(x.Id))?.EnglishTitle);
                });

            var te = string.Empty;
            te = result?.SeasonInt.ToString()?[2..] is ""
                ? result.SeasonInt.ToString()?[1..]
                : result?.SeasonInt.ToString()?[2..];
            if (result?.DescriptionMd != null) eb.AddField("Description", result.DescriptionMd.TrimTo(1024), true);
            if (result!.Genres.Any()) eb.AddField("Genres", string.Join("\n", result.Genres), true);
            if (result.CountryOfOrigin is not null) eb.AddField("Country of Origin", result.CountryOfOrigin, true);
            if (!list.Contains(null) && list.Any())
                eb.AddField("Recommendations based on this search",
                    string.Join("\n", list.Where(x => !string.IsNullOrWhiteSpace(x)).Take(10)), true);
            eb.AddField("Episodes", result.Episodes, true);
            if (result.SeasonInt is not null) eb.AddField("Seasons", te, true);
            eb.AddField("Air Start Date", result.AiringStartDate, true);
            eb.AddField("Air End Date", result.AiringEndDate, true);
            eb.AddField("Average Score", result.AverageScore, true);
            eb.AddField("Mean Score", result.MeanScore, true);
            eb.AddField("Is this Nsfw?", result.IsAdult, true);
            eb.Title = $"{result.EnglishTitle}";
            eb.Color = Mewdeko.OkColor;
            eb.WithUrl(result.SiteUrl);
            await ctx.Channel.SendMessageAsync(embed: eb.Build());
        }

        [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild)]
        public async Task Manga([Remainder] string query)
        {
            var msg = await ctx.Channel.SendConfirmAsync(
                $"<a:loading:847706744741691402> Getting results for {query}...");
            IJikan jikan = new Jikan();
            var result = await jikan.SearchMangaAsync(query);
            var paginator = new LazyPaginatorBuilder()
                .AddUser(ctx.User)
                .WithPageFactory(PageFactory)
                .WithFooter(PaginatorFooter.PageNumber | PaginatorFooter.Users)
                .WithMaxPageIndex(result.Data.Count - 1)
                .WithDefaultCanceledPage()
                .WithDefaultEmotes()
                .Build();
            await msg.DeleteAsync();
            await _interactivity.SendPaginatorAsync(paginator, Context.Channel, TimeSpan.FromMinutes(60));

            async Task<PageBuilder> PageFactory(int page)
            {
                await Task.CompletedTask;
                return new PageBuilder()
                                       .WithTitle(Format.Bold($"{result.Data.Skip(page).FirstOrDefault()?.Title}"))
                                       .AddField("First Publish Date",
                                           result.Data.Skip(page).FirstOrDefault()?.Published!)
                                       .AddField("Volumes", result.Data.Skip(page).FirstOrDefault()?.Volumes!)
                                       .AddField("Is Still Active",
                                           result.Data.Skip(page).FirstOrDefault()?.Publishing!)
                                       .AddField("Score", result.Data.Skip(page).FirstOrDefault()?.Score!)
                                       .AddField("Url", result.Data.Skip(page).FirstOrDefault()?.Url!)
                                       .WithDescription(result.Data.Skip(page).FirstOrDefault()?.Background!)
                                       .WithImageUrl(result.Data.Skip(page).FirstOrDefault()?.Images.WebP
                                                           .MaximumImageUrl!).WithColor(Mewdeko.OkColor);
            }
        }
    }
}