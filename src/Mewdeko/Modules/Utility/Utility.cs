using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Fergun.Interactive;
using Fergun.Interactive.Pagination;
using Humanizer;
using Mewdeko._Extensions;
using Mewdeko.Common;
using Mewdeko.Common.Attributes;
using Mewdeko.Database.Extensions;
using Mewdeko.Modules.Utility.Common;
using Mewdeko.Modules.Utility.Services;
using Mewdeko.Services.Impl;
using Serilog;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;

namespace Mewdeko.Modules.Utility;

public partial class Utility : MewdekoModuleBase<UtilityService>
{
    private static readonly SemaphoreSlim _sem = new(1, 1);
    private readonly DiscordSocketClient _client;
    private readonly IBotCredentials _creds;
    private readonly IStatsService _stats;
    private readonly DownloadTracker _tracker;
    private readonly InteractiveService _interactivity;
    private readonly ICoordinator _coordinator;
    private readonly GreetSettingsService _greetService;

    public Utility(
        DiscordSocketClient client,
        IStatsService stats, IBotCredentials creds, DownloadTracker tracker, InteractiveService serv, ICoordinator coordinator,
        GreetSettingsService greetService)
    {
        _coordinator = coordinator;
        _greetService = greetService;
        _interactivity = serv;
        _client = client;
        _stats = stats;
        _creds = creds;
        _tracker = tracker;
    }
    [MewdekoCommand, Usage, Description, Alias, RequireContext(ContextType.Guild), OfficialServerMod]
    public async Task Verify(IGuildUser user)
    {
        var channel = await ctx.Guild.GetTextChannelAsync(744880021692350464);
        var logChannel = await ctx.Guild.GetTextChannelAsync(945253905485430794);
        await user.AddRolesAsync(new List<ulong>() { 788884380521070614, 837761148140388382 });
        await user.RemoveRoleAsync(794777895188692992);
        await _greetService.GreetTest(channel, user);
        await logChannel.SendMessageAsync(
            $"{user.Mention} {user.Id} has been verified by {ctx.User.Mention} {ctx.User.Id}");
    }
    [MewdekoCommand, Usage, Description, Alias]
    public async Task EmoteList([Remainder] string? emotetype = null)
    {
        var emotes = emotetype switch
        {
            "animated" => ctx.Guild.Emotes.Where(x => x.Animated).ToArray(),
            "nonanimated" => ctx.Guild.Emotes.Where(x => !x.Animated).ToArray(),
            _ => ctx.Guild.Emotes.ToArray()
        };

        if (!emotes.Any())
        {
            await ctx.Channel.SendErrorAsync("No emotes found!");
            return;
        }

        var paginator = new LazyPaginatorBuilder()
            .AddUser(ctx.User)
            .WithPageFactory(PageFactory)
            .WithFooter(PaginatorFooter.PageNumber | PaginatorFooter.Users)
            .WithMaxPageIndex(emotes.Length / 10)
            .WithDefaultEmotes()
            .Build();

        await _interactivity.SendPaginatorAsync(paginator, Context.Channel,
            TimeSpan.FromMinutes(60));

        async Task<PageBuilder> PageFactory(int page)
        {
            await Task.CompletedTask;
            var titleText = emotetype switch
            {
                "animated" => $"{emotes.Length} Animated Emotes",
                "nonanimated" => $"{emotes.Length} Non Animated Emotes",
                _ =>
                    $"{emotes.Count(x => x.Animated)} Animated Emotes | {emotes.Count(x => !x.Animated)} Non Animated Emotes"
            };

            return new PageBuilder()
                                   .WithTitle(titleText)
                                   .WithDescription(string.Join("\n",
                                       emotes.OrderBy(x => x.Name).Skip(10 * page).Take(10)
                                             .Select(x => $"{x} `{x.Name}` [Link]({x.Url})")))
                                   .WithOkColor();
        }
    }

    [MewdekoCommand, Usage, Description, Aliases]
    public async Task Invite()
    {
        var eb = new EmbedBuilder()
            .AddField("Invite Link (IOS shows an error so use the browser)",
                "[Click Here](https://discord.com/oauth2/authorize?client_id=752236274261426212&scope=bot&permissions=66186303&scope=bot%20applications.commands)")
            .AddField("Website/Docs", "https://mewdeko.tech")
            .AddField("Support Server", "https://discord.gg/wB9FBMreRk")
            .WithOkColor();
        await ctx.Channel.SendMessageAsync(embed: eb.Build());
    }

    [MewdekoCommand, Usage, Description, Aliases]
    public async Task TestSite(string url)
    {
        using var client = new HttpClient();
        var response = await client.GetAsync(url);

        await response.Content.ReadAsStringAsync();
        var statusCode = response.StatusCode;
        if (statusCode.ToString() == "Forbidden")
            await ctx.Channel.SendErrorAsync("Sites down m8");
        else
            await ctx.Channel.SendConfirmAsync("Sites ok m8");
    }

    [MewdekoCommand, Usage, Description, Aliases, UserPerm(GuildPermission.ManageChannels)]
    public async Task ReactChannel(ITextChannel? chan = null)
    {
        var e = Service.GetReactChans(ctx.Guild.Id);
        if (chan == null)
        {
            if (e == 0) return;
            await Service.SetReactChan(ctx.Guild, 0);
            await ctx.Channel.SendConfirmAsync("React Channel Disabled!");
        }
        else
        {
            if (e == 0)
            {
                await Service.SetReactChan(ctx.Guild, chan.Id);
                await ctx.Channel.SendConfirmAsync($"Your React Channel has been set to {chan.Mention}!");
            }
            else
            {
                var chan2 = await ctx.Guild.GetTextChannelAsync(e);
                if (e == chan.Id)
                {
                    await ctx.Channel.SendErrorAsync("This is already your React Channel!");
                }
                else
                {
                    await Service.SetReactChan(ctx.Guild, chan.Id);
                    await ctx.Channel.SendConfirmAsync(
                        $"Your React Channel has been switched from {chan2.Mention} to {chan.Mention}!");
                }
            }
        }
    }

    [MewdekoCommand, Usage, Description, Aliases, UserPerm(GuildPermission.Administrator),
     RequireContext(ContextType.Guild)]
    public async Task SnipeSet(string yesnt)
    {
        await Service.SnipeSet(ctx.Guild, yesnt);
        var t = Service.GetSnipeSet(ctx.Guild.Id);
        await ReplyConfirmLocalizedAsync("snipe_set", t ? "Enabled" : "Disabled");
    }

    [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild)]
    public async Task Snipe()
    {
        if (!Service.GetSnipeSet(ctx.Guild.Id))
        {
            await ReplyErrorLocalizedAsync("snipe_not_enabled", Prefix);
            return;
        }

        await Service.GetSnipes(ctx.Guild.Id);
        var msg = (await Service.GetSnipes(ctx.Guild.Id)).LastOrDefault(x => x.ChannelId == ctx.Channel.Id);
        if (msg is null)
        {
            await ctx.Channel.SendErrorAsync("There is nothing to snipe here!");
            return;
        }
        var user = await ctx.Channel.GetUserAsync(msg.UserId) ?? await _client.Rest.GetUserAsync(msg.UserId);

        var em = new EmbedBuilder
        {
            Author = new EmbedAuthorBuilder
            {
                IconUrl = user.GetAvatarUrl(),
                Name = $"{user} said:"
            },
            Description = msg.Message,
            Footer = new EmbedFooterBuilder
            {
                IconUrl = ctx.User.GetAvatarUrl(),
                Text =
                    GetText("snipe_request", ctx.User.ToString(), (DateTime.UtcNow - msg.DateAdded).Humanize())
            },
            Color = Mewdeko.OkColor
        };
        await ctx.Channel.SendMessageAsync(embed: em.Build());
    }

    [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild)]
    public async Task SnipeList(int amount = 5)
    {
        if (!Service.GetSnipeSet(ctx.Guild.Id))
        {
            await ReplyErrorLocalizedAsync("snipe_not_enabled", Prefix);
            return;
        }

        var msgs = (await Service.GetSnipes(ctx.Guild.Id)).Where(x => x.ChannelId == ctx.Channel.Id && x.Edited == 0);
        {
            var snipeStores = msgs as SnipeStore[] ?? msgs.ToArray();
            if (!snipeStores.Any())
            {
                await ReplyErrorLocalizedAsync("no_snipes");
                return;
            }

            var msg = snipeStores.OrderByDescending(d => d.DateAdded).Where(x => x.Edited == 0).Take(amount);
            var paginator = new LazyPaginatorBuilder()
                .AddUser(ctx.User)
                .WithPageFactory(PageFactory)
                .WithFooter(PaginatorFooter.PageNumber | PaginatorFooter.Users)
                .WithMaxPageIndex(msg.Count() - 1)
                .WithDefaultEmotes()
                .Build();

            await _interactivity.SendPaginatorAsync(paginator, Context.Channel,
                TimeSpan.FromMinutes(60));

            async Task<PageBuilder> PageFactory(int page)
            {
                var msg1 = msg.Skip(page).FirstOrDefault();
                var user = await ctx.Channel.GetUserAsync(msg1.UserId) ??
                           await _client.Rest.GetUserAsync(msg1.UserId);
                
                return new PageBuilder()
                    .WithOkColor()
                    .WithAuthor(
                        new EmbedAuthorBuilder()
                            .WithIconUrl(user.RealAvatarUrl().AbsoluteUri)
                            .WithName($"{user} said:"))
                    .WithDescription($"{msg1.Message}")
                    .WithFooter($"\n\nMessage deleted {(DateTime.UtcNow - msg1.DateAdded).Humanize()} ago");
            }
        }
    }
    
    [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild)]
    public async Task EditSnipeList(int amount = 5)
    {
        if (!Service.GetSnipeSet(ctx.Guild.Id))
        {
            await ReplyErrorLocalizedAsync("snipe_not_enabled", Prefix);
            return;
        }

        var msgs = (await Service.GetSnipes(ctx.Guild.Id)).Where(x => x.ChannelId == ctx.Channel.Id && x.Edited == 1);
        {
            var snipeStores = msgs as SnipeStore[] ?? msgs.ToArray();
            if (!snipeStores.Any())
            {
                await ReplyErrorLocalizedAsync("no_snipes");
                return;
            }

            var msg = snipeStores.OrderByDescending(d => d.DateAdded).Where(x => x.Edited == 1).Take(amount);
            var paginator = new LazyPaginatorBuilder()
                .AddUser(ctx.User)
                .WithPageFactory(PageFactory)
                .WithFooter(PaginatorFooter.PageNumber | PaginatorFooter.Users)
                .WithMaxPageIndex(msg.Count() - 1)
                .WithDefaultEmotes()
                .Build();

            await _interactivity.SendPaginatorAsync(paginator, Context.Channel,
                TimeSpan.FromMinutes(60));

            async Task<PageBuilder> PageFactory(int page)
            {
                var msg1 = msg.Skip(page).FirstOrDefault();
                var user = await ctx.Channel.GetUserAsync(msg1.UserId) ??
                           await _client.Rest.GetUserAsync(msg1.UserId);
                
                return new PageBuilder()
                    .WithOkColor()
                    .WithAuthor(
                        new EmbedAuthorBuilder()
                            .WithIconUrl(user.RealAvatarUrl().AbsoluteUri)
                            .WithName($"{user} originally said:"))
                    .WithDescription($"{msg1.Message}")
                    .WithFooter($"\n\nMessage edited {(DateTime.UtcNow - msg1.DateAdded).Humanize()} ago");
            }
        }
    }

    [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild), Priority(1)]
    public async Task Snipe(IUser user1)
    {
        if (!Service.GetSnipeSet(ctx.Guild.Id))
        {
            await ReplyErrorLocalizedAsync("snipe_not_enabled", Prefix);
            return;
        }

        var msg = (await Service.GetSnipes(ctx.Guild.Id))
                         .FirstOrDefault(x => x.ChannelId == ctx.Channel.Id && x.UserId == user1.Id);
        if (msg is null)
        {
            await ctx.Channel.SendErrorAsync("There is nothing to snipe for this user!");
            return;
        }
        var user = await ctx.Channel.GetUserAsync(msg.UserId) ?? await _client.Rest.GetUserAsync(msg.UserId);
        var em = new EmbedBuilder
        {
            Author = new EmbedAuthorBuilder {IconUrl = user.GetAvatarUrl(), Name = $"{user} said:"},

            Description = msg.Message,
            Footer = new EmbedFooterBuilder
            {
                IconUrl = ctx.User.GetAvatarUrl(),
                Text =
                    GetText("snipe_request", ctx.User.ToString(), (DateTime.UtcNow - msg.DateAdded).Humanize())
            },
            Color = Mewdeko.OkColor
        };
        await ctx.Channel.SendMessageAsync(embed: em.Build());
    }

    [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild), Priority(2)]
    public async Task VCheck([Remainder] string? url = null)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            await ctx.Channel.SendErrorAsync("You didn't specify a url");
        }
        else
        {
            var result = await UtilityService.UrlChecker(url);
            var eb = new EmbedBuilder();
            eb.WithOkColor();
            eb.WithDescription(result.Permalink);
            eb.AddField("Virus Positives", result.Positives, true);
            eb.AddField("Number of scans", result.Total, true);
            await ctx.Channel.SendMessageAsync(embed: eb.Build());
        }
    }

    [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild), Priority(2)]
    public async Task Snipe(ITextChannel chan)
    {
        if (!Service.GetSnipeSet(ctx.Guild.Id))
        {
            await ReplyErrorLocalizedAsync("snipe_not_enabled", Prefix);
            return;
        }

        var msg = (await Service.GetSnipes(ctx.Guild.Id)).Where(x => x.Edited == 0)
                                                         .LastOrDefault(x => x.ChannelId == chan.Id);
        if (msg == null)
        {
            await ReplyErrorLocalizedAsync("no_snipes");
            return;
        }

        var user = await ctx.Channel.GetUserAsync(msg.UserId) ?? await _client.Rest.GetUserAsync(msg.UserId);

        var em = new EmbedBuilder
        {
            Author = new EmbedAuthorBuilder {IconUrl = user.GetAvatarUrl(), Name = $"{user} said:"},
            Description = msg.Message,
            Footer = new EmbedFooterBuilder
            {
                IconUrl = ctx.User.GetAvatarUrl(),
                Text =
                    GetText("snipe_request", ctx.User.ToString(), (DateTime.UtcNow - msg.DateAdded).Humanize())
            },
            Color = Mewdeko.OkColor
        };
        await ctx.Channel.SendMessageAsync(embed: em.Build());
    }

    [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild), Priority(2)]
    public async Task Snipe(ITextChannel chan, IUser user1)
    {
        if (!Service.GetSnipeSet(ctx.Guild.Id))
        {
            await ReplyErrorLocalizedAsync("snipe_not_enabled", Prefix);
            return;
        }

        var msg = (await Service.GetSnipes(ctx.Guild.Id)).Where(x => x.Edited == 0)
                                                         .LastOrDefault(x => x.ChannelId == chan.Id && x.UserId == user1.Id);
        {
            if (msg == null)
            {
                await ctx.Channel.SendErrorAsync("There's nothing to snipe for that channel and user!");
                return;
            }

            var user = await ctx.Channel.GetUserAsync(msg.UserId) ?? await _client.Rest.GetUserAsync(msg.UserId);

            var em = new EmbedBuilder
            {
                Author = new EmbedAuthorBuilder {IconUrl = user.GetAvatarUrl(), Name = $"{user} said:"},
                Description = msg.Message,
                Footer = new EmbedFooterBuilder
                {
                    IconUrl = ctx.User.GetAvatarUrl(),
                    Text =
                        GetText("snipe_request", ctx.User.ToString(), (DateTime.UtcNow - msg.DateAdded).Humanize())
                },
                Color = Mewdeko.OkColor
            };
            await ctx.Channel.SendMessageAsync(embed: em.Build());

        }
    }

    [MewdekoCommand, Usage, Description, Aliases, UserPerm(GuildPermission.Administrator),
     RequireContext(ContextType.Guild)]
    public async Task PreviewLinks(string yesnt)
    {
        await Service.PreviewLinks(ctx.Guild, yesnt[..1].ToLower());
        var t = Service.GetPLinks(ctx.Guild.Id);
        switch (t)
        {
            case 1:
                await ctx.Channel.SendConfirmAsync("Link previews are now enabled!");
                break;
            case 0:
                await ctx.Channel.SendConfirmAsync("Link Previews are now disabled!");
                break;
        }
    }

    [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild)]
    public async Task EditSnipe()
    {
        if (!Service.GetSnipeSet(ctx.Guild.Id))
        {
            await ReplyErrorLocalizedAsync("snipe_not_enabled", Prefix);
            return;
        }

        {
            var msg = (await Service.GetSnipes(ctx.Guild.Id))
                      .Where(x => x.Edited == 1)
                      .LastOrDefault(x => x.ChannelId == ctx.Channel.Id);
            if (msg == null)
            {
                await ctx.Channel.SendErrorAsync("There's nothing to snipe!");
                return;
            }

            var user = await ctx.Channel.GetUserAsync(msg.UserId) ?? await _client.Rest.GetUserAsync(msg.UserId);

            var em = new EmbedBuilder
            {
                Author = new EmbedAuthorBuilder
                {
                    IconUrl = user.GetAvatarUrl(),
                    Name = $"{user} originally said:"
                },
                Description = msg.Message,
                Footer = new EmbedFooterBuilder
                {
                    IconUrl = ctx.User.GetAvatarUrl(),
                    Text =
                        GetText("snipe_request", ctx.User.ToString(), (DateTime.UtcNow - msg.DateAdded).Humanize())
                },
                Color = Mewdeko.OkColor
            };
            await ctx.Channel.SendMessageAsync(embed: em.Build());
        }
    }

    [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild), Priority(1)]
    public async Task EditSnipe(IUser user1)
    {
        if (!Service.GetSnipeSet(ctx.Guild.Id))
        {
            await ReplyErrorLocalizedAsync("snipe_not_enabled", Prefix);
            return;
        }

        {
            var msg = (await Service.GetSnipes(ctx.Guild.Id))
                      .Where(x => x.Edited == 1)
                      .LastOrDefault(x => x.ChannelId == ctx.Channel.Id && x.UserId == user1.Id);
            if (msg == null)
            {
                await ReplyErrorLocalizedAsync("no_snipes");
                return;
            }

            var user = await ctx.Channel.GetUserAsync(msg.UserId) ?? await _client.Rest.GetUserAsync(msg.UserId);

            var em = new EmbedBuilder
            {
                Author = new EmbedAuthorBuilder
                {
                    IconUrl = user.GetAvatarUrl(),
                    Name = $"{user} originally said:"
                },
                Description = msg.Message,
                Footer = new EmbedFooterBuilder
                {
                    IconUrl = ctx.User.GetAvatarUrl(),
                    Text =
                        GetText("snipe_request", ctx.User.ToString(), (DateTime.UtcNow - msg.DateAdded).Humanize())
                },
                Color = Mewdeko.OkColor
            };
            await ctx.Channel.SendMessageAsync(embed: em.Build());
        }
    }

    [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild), Priority(1)]
    public async Task EditSnipe(ITextChannel chan)
    {
        if (!Service.GetSnipeSet(ctx.Guild.Id))
        {
            await ReplyErrorLocalizedAsync("snipe_not_enabled", Prefix);
            return;
        }

        {
            var msg = (await Service.GetSnipes(ctx.Guild.Id))
                      .Where(x => x.Edited == 1)
                      .LastOrDefault(x => x.ChannelId == chan.Id);
            if (msg == null)
            {
                await ReplyErrorLocalizedAsync("no_snipes");
                return;
            }

            var user = await ctx.Channel.GetUserAsync(msg.UserId) ?? await _client.Rest.GetUserAsync(msg.UserId);

            var em = new EmbedBuilder
            {
                Author = new EmbedAuthorBuilder
                {
                    IconUrl = user.GetAvatarUrl(),
                    Name = $"{user} originally said:"
                },
                Description = msg.Message,
                Footer = new EmbedFooterBuilder
                {
                    IconUrl = ctx.User.GetAvatarUrl(),
                    Text =
                        GetText("snipe_request", ctx.User.ToString(), (DateTime.UtcNow - msg.DateAdded).Humanize())
                },
                Color = Mewdeko.OkColor
            };
            await ctx.Channel.SendMessageAsync(embed: em.Build());
        }
    }

    [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild), Priority(1)]
    public async Task EditSnipe(ITextChannel chan, IUser user1)
    {
        if (!Service.GetSnipeSet(ctx.Guild.Id))
        {
            await ReplyErrorLocalizedAsync("snipe_not_enabled", Prefix);
            return;
        }

        {
            var msg = (await Service.GetSnipes(ctx.Guild.Id))
                      .Where(x => x.Edited == 1)
                      .LastOrDefault(x => x.ChannelId == chan.Id && x.UserId == user1.Id);
            if (msg == null)
            {
                await ReplyErrorLocalizedAsync("no_snipes");
                return;
            }

            
            var user = await ctx.Channel.GetUserAsync(msg.UserId) ?? await _client.Rest.GetUserAsync(msg.UserId);

            var em = new EmbedBuilder
            {
                Author = new EmbedAuthorBuilder
                {
                    IconUrl = user.GetAvatarUrl(),
                    Name = $"{user} originally said:"
                },
                Description = msg.Message,
                Footer = new EmbedFooterBuilder
                {
                    IconUrl = ctx.User.GetAvatarUrl(),
                    Text =
                        GetText("snipe_request", ctx.User.ToString(), (DateTime.UtcNow - msg.DateAdded).Humanize())
                },
                Color = Mewdeko.OkColor
            };
            await ctx.Channel.SendMessageAsync(embed: em.Build());
        }
    }

    [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild)]
    public async Task WhosPlaying([Remainder] string? game)
    {
        game = game?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(game))
            return;

        if (ctx.Guild is not SocketGuild socketGuild)
        {
            Log.Warning("Can't cast guild to socket guild.");
            return;
        }

        var rng = new MewdekoRandom();
        var arr = await Task.Run(() => socketGuild.Users
            .Where(u => u.Activities?.FirstOrDefault()?.Name.ToUpperInvariant() == game)
            .Select(u => u.Username)
            .OrderBy(_ => rng.Next())
            .Take(60)
            .ToArray()).ConfigureAwait(false);

        var i = 0;
        if (arr.Length == 0)
            await ReplyErrorLocalizedAsync("nobody_playing_game").ConfigureAwait(false);
        else
            await ctx.Channel.SendConfirmAsync(
                         $"```css\n{string.Join("\n", arr.GroupBy(_ => i++ / 2).Select(ig => string.Concat(ig.Select(el => $"• {el,-27}"))))}\n```")
                .ConfigureAwait(false);
    }

    [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild)]
    public async Task Vote() =>
        await ctx.Channel.EmbedAsync(new EmbedBuilder().WithOkColor()
                                                       .WithDescription(
                                                           "Vote here for Mewdeko!\n[Vote Link](https://top.gg/bot/752236274261426212)\nMake sure to join the support server! \n[Link](https://mewdeko.tech/support)"));

    [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild)]
    public async Task InRole([Remainder] IRole role)
    {
        await Context.Channel.TriggerTypingAsync().ConfigureAwait(false);
        await _tracker.EnsureUsersDownloadedAsync(ctx.Guild).ConfigureAwait(false);

        var users = await ctx.Guild.GetUsersAsync();
        var roleUsers = users
            .Where(u => u.RoleIds.Contains(role.Id))
            .ToArray();

        var paginator = new LazyPaginatorBuilder()
            .AddUser(ctx.User)
            .WithPageFactory(PageFactory)
            .WithFooter(PaginatorFooter.PageNumber | PaginatorFooter.Users)
            .WithMaxPageIndex(roleUsers.Length / 20)
            .WithDefaultEmotes()
            .Build();

        await _interactivity.SendPaginatorAsync(paginator, Context.Channel,
            TimeSpan.FromMinutes(60));

        async Task<PageBuilder> PageFactory(int page)
        {
            await Task.CompletedTask;
            return new PageBuilder().WithOkColor()
                                                    .WithTitle(
                                                        $"{Format.Bold(GetText("inrole_list", Format.Bold(role.Name)))} - {roleUsers.Length}")
                                                    .WithDescription(string.Join("\n",
                                                        roleUsers.Skip(page * 20).Take(20)
                                                                 .Select(x => $"{x} `{x.Id}`"))).AddField("User Stats",
                                                        $"<:online:914548119730024448> {roleUsers.Count(x => x.Status == UserStatus.Online)}\n<:dnd:914548634178187294> {roleUsers.Count(x => x.Status == UserStatus.DoNotDisturb)}\n<:idle:914548262424412172> {roleUsers.Count(x => x.Status == UserStatus.Idle)}\n<:offline:914548368037003355> {roleUsers.Count(x => x.Status == UserStatus.Offline)}");
        }
    }

    [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild)]
    public async Task InRoles(IRole role, IRole role2)
    {
        await Context.Channel.TriggerTypingAsync().ConfigureAwait(false);
        await _tracker.EnsureUsersDownloadedAsync(ctx.Guild).ConfigureAwait(false);
        var users = await ctx.Guild.GetUsersAsync();
        var roleUsers = users
            .Where(u => u.RoleIds.Contains(role.Id) && u.RoleIds.Contains(role2.Id))
            .Select(u => u.ToString())
            .ToArray();

        var paginator = new LazyPaginatorBuilder()
            .AddUser(ctx.User)
            .WithPageFactory(PageFactory)
            .WithFooter(PaginatorFooter.PageNumber | PaginatorFooter.Users)
            .WithMaxPageIndex(roleUsers.Length / 20)
            .WithDefaultEmotes()
            .Build();

        await _interactivity.SendPaginatorAsync(paginator, Context.Channel,
            TimeSpan.FromMinutes(60));

        async Task<PageBuilder> PageFactory(int page)
        {
            await Task.CompletedTask;
            return new PageBuilder().WithOkColor()
                                                    .WithTitle(Format.Bold(
                                                        $"Users in the roles: {role.Name} | {role2.Name} - {roleUsers.Length}"))
                                                    .WithDescription(string.Join("\n",
                                                        roleUsers.Skip(page * 20).Take(20)));
        }
    }

    [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild)]
    public async Task UserId([Remainder] IGuildUser? target = null)
    {
        var usr = target ?? ctx.User;
        await ReplyConfirmLocalizedAsync("userid", "🆔", Format.Bold(usr.ToString()),
            Format.Code(usr.Id.ToString())).ConfigureAwait(false);
    }

    [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild)]
    public async Task RoleId([Remainder] IRole role) =>
        await ReplyConfirmLocalizedAsync("roleid", "🆔", Format.Bold(role.ToString()),
            Format.Code(role.Id.ToString())).ConfigureAwait(false);

    [MewdekoCommand, Usage, Description, Aliases]
    public async Task ChannelId() =>
        await ReplyConfirmLocalizedAsync("channelid", "🆔", Format.Code(ctx.Channel.Id.ToString()))
            .ConfigureAwait(false);

    [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild)]
    public async Task ServerId() =>
        await ReplyConfirmLocalizedAsync("serverid", "🆔", Format.Code(ctx.Guild.Id.ToString()))
            .ConfigureAwait(false);

    [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild)]
    public async Task Roles(IGuildUser? target, int page = 1)
    {
        var channel = (ITextChannel) ctx.Channel;
        var guild = channel.Guild;

        const int rolesPerPage = 20;

        if (page is < 1 or > 100)
            return;

        if (target != null)
        {
            var roles = target.GetRoles().Except(new[] {guild.EveryoneRole}).OrderBy(r => -r.Position)
                .Skip((page - 1) * rolesPerPage).Take(rolesPerPage).ToArray();
            if (!roles.Any())
                await ReplyErrorLocalizedAsync("no_roles_on_page").ConfigureAwait(false);
            else
                await channel.SendConfirmAsync(GetText("roles_page", page, Format.Bold(target.ToString())),
                    $"\n• {string.Join("\n• ", (IEnumerable<IRole>)roles)}").ConfigureAwait(false);
        }
        else
        {
            var roles = guild.Roles.Except(new[] {guild.EveryoneRole}).OrderBy(r => -r.Position)
                .Skip((page - 1) * rolesPerPage).Take(rolesPerPage).ToArray();
            if (!roles.Any())
                await ReplyErrorLocalizedAsync("no_roles_on_page").ConfigureAwait(false);
            else
                await channel.SendConfirmAsync(GetText("roles_all_page", page),
                                 $"\n• {string.Join("\n• ", (IEnumerable<IRole>)roles).SanitizeMentions()}")
                    .ConfigureAwait(false);
        }
    }

    [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild)]
    public Task Roles(int page = 1) => Roles(null, page);

    [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild)]
    public async Task ChannelTopic([Remainder] ITextChannel? channel = null)
    {
        channel ??= (ITextChannel)ctx.Channel;

        var topic = channel.Topic;
        if (string.IsNullOrWhiteSpace(topic))
            await ReplyErrorLocalizedAsync("no_topic_set").ConfigureAwait(false);
        else
            await ctx.Channel.SendConfirmAsync(GetText("channel_topic"), topic).ConfigureAwait(false);
    }

    [MewdekoCommand, Usage, Description, Aliases]
    public async Task Stats()
    {
        var user = await _client.Rest.GetUserAsync(280835732728184843);
        await ctx.Channel.EmbedAsync(
                     new EmbedBuilder().WithOkColor()
                                       .WithAuthor(eab => eab.WithName($"{_client.CurrentUser.Username} v{StatsService.BOT_VERSION}")
                                                             .WithUrl("https://discord.gg/6n3aa9Xapf")
                                                             .WithIconUrl(_client.CurrentUser.GetAvatarUrl()))
                                       .AddField(efb =>
                                           efb.WithName(GetText("author")).WithValue($"{user.Username}#{user.Discriminator}")
                                              .WithIsInline(false))
                                       .AddField(efb => efb.WithName("Library").WithValue(_stats.Library).WithIsInline(false))
                                       .AddField(efb =>
                                           efb.WithName(GetText("shard")).WithValue($"#{_client.ShardId} / {_creds.TotalShards}")
                                              .WithIsInline(false))
                                       .AddField(efb => efb.WithName(GetText("memory")).WithValue($"{_stats.Heap} MB").WithIsInline(false))
                                       .AddField(efb =>
                                           efb.WithName(GetText("uptime")).WithValue(_stats.GetUptimeString("\n")).WithIsInline(false))
                                       .AddField(efb => efb.WithName("Servers").WithValue($"{_coordinator.GetGuildCount()} Servers").WithIsInline(false)))
                 .ConfigureAwait(false);
    }

    [MewdekoCommand, Usage, Description, Aliases]
    public async Task Showemojis([Remainder] string _)
    {
        var tags = ctx.Message.Tags.Where(t => t.Type == TagType.Emoji).Select(t => (Emote) t.Value);

        var result = string.Join("\n", tags.Select(m => GetText("showemojis", m, m.Url)));

        if (string.IsNullOrWhiteSpace(result))
            await ReplyErrorLocalizedAsync("showemojis_none").ConfigureAwait(false);
        else
            await ctx.Channel.SendMessageAsync(result.TrimTo(2000)).ConfigureAwait(false);
    }


    [MewdekoCommand, Usage, Description, Aliases, Ratelimit(30)]
    public async Task Ping()
    {
        await _sem.WaitAsync(5000).ConfigureAwait(false);
        try
        {
            var sw = Stopwatch.StartNew();
            var msg = await ctx.Channel.SendMessageAsync("🏓").ConfigureAwait(false);
            sw.Stop();
            msg.DeleteAfter(0);

            await ctx.Channel
                .SendConfirmAsync(
                    $"Bot Ping {(int) sw.Elapsed.TotalMilliseconds}ms\nBot Latency {((DiscordSocketClient) ctx.Client).Latency}ms")
                .ConfigureAwait(false);
        }
        finally
        {
            _sem.Release();
        }
    }
}