using CodeHollow.FeedReader;
using Discord;
using Discord.Commands;
using Fergun.Interactive;
using Fergun.Interactive.Pagination;
using Mewdeko._Extensions;
using Mewdeko.Common;
using Mewdeko.Common.Attributes;
using Mewdeko.Modules.Searches.Services;
using Serilog;

namespace Mewdeko.Modules.Searches;

public partial class Searches
{
    [Group]
    public class FeedCommands : MewdekoSubmodule<FeedsService>
    {
        private readonly InteractiveService _interactivity;

        public FeedCommands(InteractiveService serv) => _interactivity = serv;

        [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild),
         UserPerm(GuildPermission.ManageMessages)]
        public async Task Feed(string url, [Remainder] ITextChannel? channel = null)
        {
            var success = Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                          (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
            if (success)
            {
                channel ??= (ITextChannel) ctx.Channel;
                try
                {
                    await FeedReader.ReadAsync(url).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log.Information(ex, "Unable to get feeds from that url");
                    success = false;
                }
            }

            if (success)
            {
                success = Service.AddFeed(ctx.Guild.Id, channel.Id, url);
                if (success)
                {
                    await ReplyConfirmLocalizedAsync("feed_added").ConfigureAwait(false);
                    return;
                }
            }

            await ReplyErrorLocalizedAsync("feed_not_valid").ConfigureAwait(false);
        }

        [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild),
         UserPerm(GuildPermission.ManageMessages)]
        public async Task FeedRemove(int index)
        {
            if (Service.RemoveFeed(ctx.Guild.Id, --index))
                await ReplyConfirmLocalizedAsync("feed_removed").ConfigureAwait(false);
            else
                await ReplyErrorLocalizedAsync("feed_out_of_range").ConfigureAwait(false);
        }

        [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild),
         UserPerm(GuildPermission.ManageMessages)]
        public async Task FeedMessage(int index, [Remainder]string message)
        {
            if (await Service.AddFeedMessage(ctx.Guild.Id, --index, message))
                await ReplyConfirmLocalizedAsync("feed_msg_updated").ConfigureAwait(false);
            else
                await ReplyErrorLocalizedAsync("feed_out_of_range").ConfigureAwait(false);
        }
        [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild),
         UserPerm(GuildPermission.ManageMessages)]
        public async Task FeedList()
        {
            var feeds = Service.GetFeeds(ctx.Guild.Id);

            if (!feeds.Any())
            {
                await ctx.Channel.EmbedAsync(new EmbedBuilder()
                        .WithOkColor()
                        .WithDescription(GetText("feed_no_feed")))
                    .ConfigureAwait(false);
                return;
            }

            var paginator = new LazyPaginatorBuilder()
                .AddUser(ctx.User)
                .WithPageFactory(PageFactory)
                .WithFooter(PaginatorFooter.PageNumber | PaginatorFooter.Users)
                .WithMaxPageIndex(feeds.Count / 10)
                .WithDefaultEmotes()
                .Build();

            await _interactivity.SendPaginatorAsync(paginator, Context.Channel, TimeSpan.FromMinutes(60));

            async Task<PageBuilder> PageFactory(int page)
            {
                await Task.CompletedTask;
                var embed = new PageBuilder()
                        .WithOkColor();
                    var i = 0;
                    var fs = string.Join("\n", feeds.Skip(page * 10)
                        .Take(10)
                        .Select(x => $"`{(page * 10) + ++i}.` <#{x.ChannelId}> {x.Url}"));

                    return embed.WithDescription(fs);
            }
        }
    }
}