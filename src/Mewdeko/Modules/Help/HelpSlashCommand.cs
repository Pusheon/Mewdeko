using Discord;
using Discord.Commands;
using Discord.Interactions;
using Discord.WebSocket;
using Fergun.Interactive;
using Fergun.Interactive.Pagination;
using Mewdeko._Extensions;
using Mewdeko.Common;
using Mewdeko.Common.Attributes;
using Mewdeko.Modules.Help.Services;
using Mewdeko.Modules.Permissions.Services;

namespace Mewdeko.Modules.Help;

public class HelpSlashCommand : MewdekoSlashModuleBase<HelpService>
{
    private readonly InteractiveService _interactivity;
    private readonly IServiceProvider _serviceProvider;
    private readonly GlobalPermissionService _permissionService;
    private readonly CommandService _cmds;

    public HelpSlashCommand(
        GlobalPermissionService permissionService,
        InteractiveService interactivity,
        IServiceProvider serviceProvider,
        CommandService cmds)
    {
        _permissionService = permissionService;
        _interactivity = interactivity;
        _serviceProvider = serviceProvider;
        _cmds = cmds;
    }
    
    [SlashCommand("help", "Shows help on how to use the bot"), BlacklistCheck]
    public async Task Modules()
    {
        var embed = new EmbedBuilder();
        embed.WithAuthor(new EmbedAuthorBuilder().WithIconUrl(ctx.Client.CurrentUser.RealAvatarUrl().ToString())
            .WithName("Mewdeko Help Menu"));
        embed.WithColor(Mewdeko.OkColor);
        embed.WithAuthor(new EmbedAuthorBuilder().WithIconUrl(ctx.Client.CurrentUser.RealAvatarUrl().ToString())
                                                 .WithName("Mewdeko Help Menu"));
        embed.WithColor(Mewdeko.OkColor);
        embed.WithDescription(
            $"\nDo `{Prefix}help command` to see a description of a command you need more info on! For example {Prefix}h afk");
        embed.AddField("**Categories**",
            $">  `{Prefix}cmds Administration`\n>  `{Prefix}cmds Moderation`\n>  `{Prefix}cmds Utility`\n>  `{Prefix}cmds Suggestions`\n>  `{Prefix}cmds Server Management`\n>  `{Prefix}cmds Permissions`\n>  `{Prefix}cmds Xp`\n>  `{Prefix}cmds Afk`\n>  `{Prefix}cmds Confessions`\n>  `{Prefix}cmds Starboard`",
            true);
        embed.AddField("_ _",
            $">  `{Prefix}cmds Music`\n>  `{Prefix}cmds Gambling`\n>  `{Prefix}cmds Searches`\n>  `{Prefix}cmds Games`\n>  `{Prefix}cmds Help`\n>  `{Prefix}cmds ChatTriggers`\n>  `{Prefix}cmds Giveaways`\n>  `{Prefix}cmds MultiGreet`\n> `{Prefix}cmds Highlights`",
            true);
               await ctx.Interaction.RespondAsync(embed: embed.Build(), components: Service.Builder.Build());
    }

    [ComponentInteraction("helpselect")]
    public async Task HelpSlash(string[] selected)
    {
        var currentmsg = Service.GetUserMessage(ctx.User);
        if (currentmsg is null)
        {
            await ctx.Interaction.SendEphemeralErrorAsync("Please run the help command again to use this!");
            return;
        }
        var module = selected.FirstOrDefault();
        module = module?.Trim().ToUpperInvariant().Replace(" ", "");
        if (string.IsNullOrWhiteSpace(module))
        {
            await Modules();
            return;
        }


        // Find commands for that module
        // don't show commands which are blocked
        // order by name
        var cmds = _cmds.Commands.Where(c =>
                c.Module.GetTopLevelModule().Name.ToUpperInvariant()
                    .StartsWith(module, StringComparison.InvariantCulture))
            .Where(c => !_permissionService.BlockedCommands.Contains(c.Aliases[0].ToLowerInvariant()))
            .OrderBy(c => c.Aliases[0])
            .Distinct(new CommandTextEqualityComparer());
        var context = new CommandContext(ctx.Client, currentmsg);
        // check preconditions for all commands, but only if it's not 'all'
        // because all will show all commands anyway, no need to check
        var succ = new HashSet<CommandInfo>((await Task.WhenAll(cmds.Select(async x =>
            {
                var pre = await x.CheckPreconditionsAsync(new CommandContext(ctx.Client, currentmsg), _serviceProvider).ConfigureAwait(false);
                return (Cmd: x, Succ: pre.IsSuccess);
            })).ConfigureAwait(false))
            .Where(x => x.Succ)
            .Select(x => x.Cmd));

        var cmdsWithGroup = cmds
            .GroupBy(c => c.Module.Name.Replace("Commands", "", StringComparison.InvariantCulture))
            .OrderBy(x => x.Key == x.First().Module.Name ? int.MaxValue : x.Count());

        if (!cmds.Any())
        {
            await ReplyErrorLocalizedAsync("module_not_found_or_cant_exec").ConfigureAwait(false);
            return;
        }

        var i = 0;
        var groups = cmdsWithGroup.GroupBy(_ => i++ / 48).ToArray();
        var paginator = new LazyPaginatorBuilder()
            .AddUser(ctx.User)
            .WithPageFactory(PageFactory)
            .WithFooter(PaginatorFooter.PageNumber | PaginatorFooter.Users)
            .WithMaxPageIndex(groups.Select(x => x.Count()).FirstOrDefault() - 1)
            .WithDefaultEmotes()
            .Build();

        await _interactivity.SendPaginatorAsync(paginator, ctx.Interaction as SocketInteraction,
            TimeSpan.FromMinutes(60));

        async Task<PageBuilder> PageFactory(int page)
        {
            await Task.CompletedTask;
            var transformed = groups.Select(x => x.ElementAt(page).Select(x =>
                    $"{(succ.Contains(x) ? "✅" : "❌")}{Prefix + x.Aliases.First(),-15} {$"[{x.Aliases.Skip(1).FirstOrDefault()}]",-8}"))
                .FirstOrDefault();
            var last = groups.Select(x => x.Count()).FirstOrDefault();
            for (i = 0; i < last; i++)
                if (i == last - 1 && (i + 1) % 1 != 0)
                {
                    var grp = 0;
                    var count = transformed.Count();
                    transformed = transformed
                        .GroupBy(_ => grp++ % count / 2)
                        .Select(x =>
                        {
                            if (x.Count() == 1)
                                return $"{x.First()}";
                            return string.Concat(x);
                        });
                }

            return new PageBuilder()
                .AddField(groups.Select(x => x.ElementAt(page).Key).FirstOrDefault(),
                    $"```css\n{string.Join("\n", transformed)}\n```")
                .WithDescription(
                    $"🔹: Your current prefix is {Format.Code(Prefix)}\n✅: You can use this command.\n❌: You cannot use this command.\n<:Nekoha_Oooo:866320687810740234>: If you need any help don't hesitate to join [The Support Server](https://discord.gg/wB9FBMreRk)\nDo `{Prefix}h commandname` to see info on that command")
                .WithOkColor();
        }
    }
    [SlashCommand("invite", "You should invite me to your server and check all my features!"), BlacklistCheck]
    public async Task Invite()
    {
        var eb = new EmbedBuilder()
            .AddField("Invite Link",
                "[Click Here](https://discord.com/oauth2/authorize?client_id=752236274261426212&scope=bot&permissions=66186303&scope=bot%20applications.commands)")
            .AddField("Website/Docs", "https://mewdeko.tech")
            .AddField("Support Server", "https://discord.gg/wB9FBMreRk")
            .WithOkColor();
        await ctx.Interaction.RespondAsync(embed: eb.Build());
    }
}