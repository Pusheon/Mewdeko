﻿using Discord.Interactions;
using Fergun.Interactive;
using Fergun.Interactive.Pagination;
using Humanizer;
using Mewdeko.Common.Attributes.SlashCommands;
using Mewdeko.Modules.Utility.Common;
using Mewdeko.Modules.Utility.Services;
using System.Threading.Tasks;

namespace Mewdeko.Modules.Utility;

public partial class Utility
{
    [Group("snipe", "Snipe edited or delete messages!")]
    public class SlashSnipes : MewdekoSlashModuleBase<UtilityService>
    {
        private readonly DiscordSocketClient _client;
        private readonly InteractiveService _interactivity;
        private readonly GuildSettingsService _guildSettings;

        public SlashSnipes(DiscordSocketClient client, InteractiveService interactiveService, GuildSettingsService guildSettings)
        {
            _client = client;
            _interactivity = interactiveService;
            _guildSettings = guildSettings;
        }

        [SlashCommand("deleted", "Snipes deleted messages for the current or mentioned channel"),
         RequireContext(ContextType.Guild), CheckPermissions]
        public async Task Snipe(IMessageChannel? channel = null, IUser? user = null)
        {
            channel ??= ctx.Channel;
            if (!Service.GetSnipeSet(ctx.Guild.Id))
            {
                await ReplyErrorLocalizedAsync("snipe_slash_not_enabled");
                return;
            }

            var msg = (await Service.GetSnipes(ctx.Guild.Id)).Where(x => x.Edited == 0)
                             .LastOrDefault(x => x.ChannelId == channel.Id);

            if (user is not null)
            {
                msg = (await Service.GetSnipes(ctx.Guild.Id)).Where(x => x.Edited == 0)
                                                             .LastOrDefault(x => x.ChannelId == channel.Id && x.UserId == user.Id);
            }

            if (msg is null)
            {
                await ReplyErrorLocalizedAsync("no_snipes");
                return;
            }

            user = await ctx.Channel.GetUserAsync(msg.UserId) ?? await _client.Rest.GetUserAsync(msg.UserId);

            var em = new EmbedBuilder
            {
                Author = new EmbedAuthorBuilder { IconUrl = user.GetAvatarUrl(), Name = $"{user} said:" },
                Description = msg.Message,
                Footer = new EmbedFooterBuilder
                {
                    IconUrl = ctx.User.GetAvatarUrl(),
                    Text =
                        GetText("snipe_request", ctx.User.ToString(), (DateTime.UtcNow - msg.DateAdded).Humanize())
                },
                Color = Mewdeko.OkColor
            };
            await ctx.Interaction.RespondAsync(embed: em.Build());
        }

        [SlashCommand("edited", "Snipes edited messages for the current or mentioned channel"),
         RequireContext(ContextType.Guild), CheckPermissions]
        public async Task EditSnipe(IMessageChannel? channel = null, IUser? user = null)
        {
            channel ??= ctx.Channel;
            if (!Service.GetSnipeSet(ctx.Guild.Id))
            {
                await ReplyErrorLocalizedAsync("snipe_slash_not_enabled");
                return;
            }

            var msg = (await Service.GetSnipes(ctx.Guild.Id)).Where(x => x.Edited == 1)
                                                             .LastOrDefault(x => x.ChannelId == channel.Id);

            if (user is not null)
            {
                msg = (await Service.GetSnipes(ctx.Guild.Id)).Where(x => x.Edited == 1)
                                                             .LastOrDefault(x => x.ChannelId == channel.Id && x.UserId == user.Id);
            }

            if (msg is null)
            {
                await ReplyErrorLocalizedAsync("no_snipes");
                return;
            }

            user = await ctx.Channel.GetUserAsync(msg.UserId) ?? await _client.Rest.GetUserAsync(msg.UserId);

            var em = new EmbedBuilder
            {
                Author = new EmbedAuthorBuilder { IconUrl = user.GetAvatarUrl(), Name = $"{user} said:" },
                Description = msg.Message,
                Footer = new EmbedFooterBuilder
                {
                    IconUrl = ctx.User.GetAvatarUrl(),
                    Text =
                        GetText("snipe_request", ctx.User.ToString(), (DateTime.UtcNow - msg.DateAdded).Humanize())
                },
                Color = Mewdeko.OkColor
            };
            await ctx.Interaction.RespondAsync(embed: em.Build());
        }

        [SlashCommand("deletedlist", "Lists the last 5 delete snipes unless specified otherwise."),
         RequireContext(ContextType.Guild), CheckPermissions]
        public async Task SnipeList(int amount = 5)
        {
            if (!Service.GetSnipeSet(ctx.Guild.Id))
            {
                await ctx.Channel.SendErrorAsync(
                    $"Sniping is not enabled in this server! Use `{_guildSettings.GetPrefix(ctx.Guild)}snipeset enable` to enable it!");
                return;
            }

            var msgs = (await Service.GetSnipes(ctx.Guild.Id))
                              .Where(x => x.ChannelId == ctx.Channel.Id && x.Edited == 0);
            {
                var snipeStores = msgs as SnipeStore[] ?? msgs.ToArray();
                if (snipeStores.Length == 0)
                {
                    await ctx.Interaction.SendErrorAsync("There's nothing to snipe!");
                    return;
                }

                var msg = snipeStores.OrderByDescending(d => d.DateAdded).Where(x => x.Edited == 0).Take(amount);
                var paginator = new LazyPaginatorBuilder().AddUser(ctx.User).WithPageFactory(PageFactory)
                                                          .WithFooter(
                                                              PaginatorFooter.PageNumber | PaginatorFooter.Users)
                                                          .WithMaxPageIndex(msg.Count() - 1).WithDefaultEmotes()
            .WithActionOnCancellation(ActionOnStop.DeleteMessage)
                                                          .Build();

                await _interactivity.SendPaginatorAsync(paginator, (ctx.Interaction as SocketInteraction)!, TimeSpan.FromMinutes(60));

                async Task<PageBuilder> PageFactory(int page)
                {
                    var msg1 = msg.Skip(page).FirstOrDefault();
                    var user = await ctx.Channel.GetUserAsync(msg1.UserId)
                               ?? await _client.Rest.GetUserAsync(msg1.UserId);

                    return new PageBuilder().WithOkColor()
                                                            .WithAuthor(new EmbedAuthorBuilder()
                                                                        .WithIconUrl(user.RealAvatarUrl().AbsoluteUri)
                                                                        .WithName($"{user} said:"))
                                                            .WithDescription(
                                                                $"{msg1.Message}\n\nMessage deleted {(DateTime.UtcNow - msg1.DateAdded).Humanize()} ago");
                }
            }
        }

        [SlashCommand("editedlist", "Lists the last 5 edit snipes unless specified otherwise."),
         RequireContext(ContextType.Guild), CheckPermissions]
        public async Task EditSnipeList(int amount = 5)
        {
            if (!Service.GetSnipeSet(ctx.Guild.Id))
            {
                await ctx.Channel.SendErrorAsync(
                    $"Sniping is not enabled in this server! Use `{_guildSettings.GetPrefix(ctx.Guild)}snipeset enable` to enable it!");
                return;
            }

            var msgs = (await Service.GetSnipes(ctx.Guild.Id))
                              .Where(x => x.ChannelId == ctx.Channel.Id && x.Edited == 1);
            {
                var snipeStores = msgs as SnipeStore[] ?? msgs.ToArray();
                if (snipeStores.Length == 0)
                {
                    await ctx.Interaction.SendErrorAsync("There's nothing to snipe!");
                    return;
                }

                var msg = snipeStores.OrderByDescending(d => d.DateAdded).Where(x => x.Edited == 1).Take(amount);
                var paginator = new LazyPaginatorBuilder().AddUser(ctx.User).WithPageFactory(PageFactory)
                                                          .WithFooter(
                                                              PaginatorFooter.PageNumber | PaginatorFooter.Users)
                                                          .WithMaxPageIndex(msg.Count() - 1).WithDefaultEmotes()
            .WithActionOnCancellation(ActionOnStop.DeleteMessage)
                                                          .Build();

                await _interactivity.SendPaginatorAsync(paginator, (ctx.Interaction as SocketInteraction)!, TimeSpan.FromMinutes(60));

                async Task<PageBuilder> PageFactory(int page)
                {
                    var msg1 = msg.Skip(page).FirstOrDefault();
                    var user = await ctx.Channel.GetUserAsync(msg1.UserId)
                               ?? await _client.Rest.GetUserAsync(msg1.UserId);

                    return new PageBuilder().WithOkColor()
                                                            .WithAuthor(new EmbedAuthorBuilder()
                                                                        .WithIconUrl(user.RealAvatarUrl().AbsoluteUri)
                                                                        .WithName($"{user} said:"))
                                                            .WithDescription(
                                                                $"{msg1.Message}\n\nMessage deleted {(DateTime.UtcNow - msg1.DateAdded).Humanize()} ago");
                }
            }
        }

        [SlashCommand("set", "Enable or Disable sniping"),
         SlashUserPerm(GuildPermission.Administrator),
         CheckPermissions]
        public async Task SnipeSet(bool enabled)
        {
            await Service.SnipeSetBool(ctx.Guild, enabled);
            var t = Service.GetSnipeSet(ctx.Guild.Id);
            await ReplyConfirmLocalizedAsync("snipe_set", t ? "Enabled" : "Disabled");
        }
    }
}