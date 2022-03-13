using System.IO;
using System.Text;
using Amazon.S3;
using Amazon.S3.Model;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Fergun.Interactive;
using Fergun.Interactive.Pagination;
using Mewdeko._Extensions;
using Mewdeko.Common;
using Mewdeko.Common.Attributes;
using Mewdeko.Common.Replacements;
using Mewdeko.Modules.Help.Common;
using Mewdeko.Modules.Help.Services;
using Mewdeko.Modules.Permissions.Services;
using Mewdeko.Services.Settings;
using Mewdeko.Services.strings;
using Newtonsoft.Json;
using Swan;
using cinfo = System.Collections.Generic.KeyValuePair<string, string>;

namespace Mewdeko.Modules.Help;

public class Help : MewdekoModuleBase<HelpService>
{
    private readonly BotConfigService _bss;
    private readonly DiscordSocketClient _client;
    private readonly CommandService _cmds;
    private readonly InteractiveService _interactive;
    private readonly AsyncLazy<ulong> _lazyClientId;
    private readonly GlobalPermissionService _perms;
    private readonly IServiceProvider _services;
    private readonly IBotStrings _strings;

    public Help(GlobalPermissionService perms, CommandService cmds, BotConfigService bss,
        IServiceProvider services, DiscordSocketClient client, IBotStrings strings,
        InteractiveService serv)
    {
        _interactive = serv;
        _cmds = cmds;
        _bss = bss;
        _perms = perms;
        _services = services;
        _client = client;
        _strings = strings;
        _lazyClientId = new AsyncLazy<ulong>(async () => (await _client.GetApplicationInfoAsync()).Id);
    }

    public async Task<(string plainText, EmbedBuilder embed)> GetHelpStringEmbed()
    {
        var botSettings = _bss.Data;
        if (string.IsNullOrWhiteSpace(botSettings.HelpText) || botSettings.HelpText == "-")
            return default;

        var clientId = await _lazyClientId.Value;
        var r = new ReplacementBuilder()
            .WithDefault(Context)
            .WithOverride("{0}", () => clientId.ToString())
            .WithOverride("{1}", () => Prefix)
            .WithOverride("%prefix%", () => Prefix)
            .WithOverride("%bot.prefix%", () => Prefix)
            .Build();

        if (SmartEmbed.TryParse(r.Replace(botSettings.HelpText), out var embed, out var plainText))
            return (plainText, embed);
        var eb = new EmbedBuilder().WithOkColor()
                                   .WithDescription(string.Format(botSettings.HelpText, clientId, Prefix));
        return (plainText, eb);

    }

    [MewdekoCommand, Usage, Description, Aliases]
    public async Task SearchCommand(string commandname)
    {
        var list = new List<cinfo>();
        var cmds = _cmds.Commands.Where(c => c.Name.Contains(commandname, StringComparison.InvariantCulture));
        if (!cmds.Any())
        {
            await ctx.Channel.SendErrorAsync(
                "That command wasn't found! Please retry your search with a different term.");
        }
        else
        {
            list.AddRange(cmds.Select(i =>
                new cinfo($"{i.Name}", i.RealSummary(_strings, ctx.Guild.Id, Prefix).Truncate(50))));

            var eb = new EmbedBuilder()
                .WithOkColor()
                .AddField("Command", string.Join("\n", list.Select(x => x.Key)), true)
                .AddField("Description", string.Join("\n", list.Select(x => x.Value)), true);
            await ctx.Channel.SendMessageAsync(embed: eb.Build());
        }
    }

    [MewdekoCommand, Usage, Description, Aliases]
    public async Task Modules()
    {
        var embed = new EmbedBuilder();
        embed.WithAuthor(new EmbedAuthorBuilder().WithIconUrl(ctx.Client.CurrentUser.RealAvatarUrl().ToString())
            .WithName("Mewdeko Help Menu"));
        embed.WithColor(Mewdeko.OkColor);
        embed.WithDescription(
            $"\nDo `{Prefix}help command` to see a description of a command you need more info on! For example {Prefix}h afk");
        embed.AddField("**Categories**",
            $">  `{Prefix}cmds Administration`\n>  `{Prefix}cmds Moderation`\n>  `{Prefix}cmds Utility`\n>  `{Prefix}cmds Suggestions`\n>  `{Prefix}cmds Server Management`\n>  `{Prefix}cmds Permissions`\n>  `{Prefix}cmds Xp`\n>  `{Prefix}cmds Afk`\n>  `{Prefix}cmds Confessions`\n>  `{Prefix}cmds Starboard`",
            true);
        embed.AddField("_ _",
            $">  `{Prefix}cmds Nsfw`\n>  `{Prefix}cmds Music`\n>  `{Prefix}cmds Gambling`\n>  `{Prefix}cmds Searches`\n>  `{Prefix}cmds Games`\n>  `{Prefix}cmds Help`\n>  `{Prefix}cmds ChatTriggers`\n>  `{Prefix}cmds Giveaways`\n>  `{Prefix}cmds MultiGreet`\n> `{Prefix}cmds Highlights`",
            true);
        embed.AddField(" Links",
            "[Documentation](https://mewdeko.tech) | [Support Server](https://discord.gg/wB9FBMreRk) | [Invite Me](https://discord.com/oauth2/authorize?client_id=752236274261426212&scope=bot&permissions=66186303&scope=bot%20applications.commands) | [Top.gg Listing](https://top.gg/bot/752236274261426212) | [Donate!](https://ko-fi.com/mewdeko) ");
        
        await ctx.Channel.SendMessageAsync(embed: embed.Build(), components: Service.Builder.Build());
        await Service.AddUser(ctx.Message, DateTime.UtcNow);
    }
    
    [MewdekoCommand, Usage, Description, Aliases]
    public async Task Donate() =>
        await ctx.Channel.SendConfirmAsync(
            "If you would like to support the project, here's how:\nKo-Fi: https://ko-fi.com/mewdeko\nI appreciate any donations as they will help improve Mewdeko for the better!");

    [MewdekoCommand, Usage, Description, Aliases, MewdekoOptions(typeof(CommandsOptions))]
    public async Task Commands([Remainder] string? module = null)
    {
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
            .Where(c => !_perms.BlockedCommands.Contains(c.Aliases[0].ToLowerInvariant()))
            .OrderBy(c => c.Aliases[0])
            .Distinct(new CommandTextEqualityComparer());


        // check preconditions for all commands, but only if it's not 'all'
        // because all will show all commands anyway, no need to check
        var succ = new HashSet<CommandInfo>((await Task.WhenAll(cmds.Select(async x =>
            {
                var pre = await x.CheckPreconditionsAsync(Context, _services).ConfigureAwait(false);
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

        await _interactive.SendPaginatorAsync(paginator, Context.Channel,
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
                    $"<:Nekoha_Hmm:866320787865731093>: Your current prefix is {Format.Code(Prefix)}\n✅: You can use this command.\n❌: You cannot use this command.\n<:Nekoha_Oooo:866320687810740234>: If you need any help don't hesitate to join [The Support Server](https://discord.gg/wB9FBMreRk)\nDo `{Prefix}h commandname` to see info on that command")
                .WithOkColor();
        }
    }

    [MewdekoCommand, Usage, Description, Aliases, Priority(0)]
    public async Task H([Remainder] string fail)
    {
        var prefixless =
            _cmds.Commands.FirstOrDefault(x => x.Aliases.Any(cmdName => cmdName.ToLowerInvariant() == fail));
        if (prefixless != null)
        {
            await H(prefixless).ConfigureAwait(false);
            return;
        }

        await ReplyErrorLocalizedAsync("command_not_found").ConfigureAwait(false);
    }

    [MewdekoCommand, Usage, Description, Aliases, Priority(1)]
    public async Task H([Remainder] CommandInfo? com = null)
    {
        var channel = ctx.Channel;

        if (com == null)
        {
            await Modules();
            return;
        }

        var embed = Service.GetCommandHelp(com, ctx.Guild);
        await channel.EmbedAsync(embed).ConfigureAwait(false);
    }

    [MewdekoCommand, Usage, Description, Aliases, OwnerOnly]
    public async Task GenCmdList([Remainder] string? path = null)
    {
        _ = ctx.Channel.TriggerTypingAsync();

        // order commands by top level module name
        // and make a dictionary of <ModuleName, Array<JsonCommandData>>
        var cmdData = _cmds
            .Commands
            .GroupBy(x => x.Module.GetTopLevelModule().Name)
            .OrderBy(x => x.Key)
            .ToDictionary(
                x => x.Key,
                x => x.Distinct(x => x.Aliases.First())
                    .Select(com =>
                    {
                        com.Module.GetTopLevelModule();
                        List<string> optHelpStr = null!;
                        var opt = ((MewdekoOptionsAttribute) com.Attributes.FirstOrDefault(x =>
                            x is MewdekoOptionsAttribute))?.OptionType;
                        if (opt != null) optHelpStr = HelpService.GetCommandOptionHelpList(opt);

                        return new CommandJsonObject
                        {
                            Aliases = com.Aliases.Select(alias => Prefix + alias).ToArray(),
                            Description = com.RealSummary(_strings, ctx.Guild?.Id, Prefix),
                            Usage = com.RealRemarksArr(_strings, ctx.Guild?.Id, Prefix),
                            Submodule = com.Module.Name,
                            Module = com.Module.GetTopLevelModule().Name,
                            Options = optHelpStr,
                            Requirements = HelpService.GetCommandRequirements(com)
                        };
                    })
                    .ToList()
            );

        var readableData = JsonConvert.SerializeObject(cmdData, Formatting.Indented);
        var uploadData = JsonConvert.SerializeObject(cmdData, Formatting.None);

        // for example https://nyc.digitaloceanspaces.com (without your space name)
        var serviceUrl = Environment.GetEnvironmentVariable("do_spaces_address");

        // generate spaces access key on https://cloud.digitalocean.com/account/api/tokens
        // you will get 2 keys, first, shorter one is id, longer one is secret
        var accessKey = Environment.GetEnvironmentVariable("do_access_key_id");
        var secretAcccessKey = Environment.GetEnvironmentVariable("do_access_key_secret");

        // if all env vars are set, upload the unindented file (to save space) there
        if (!(serviceUrl is null || accessKey is null || secretAcccessKey is null))
        {
            var config = new AmazonS3Config {ServiceURL = serviceUrl};
            using var client = new AmazonS3Client(accessKey, secretAcccessKey, config);
            await client.PutObjectAsync(new PutObjectRequest
            {
                BucketName = "Mewdeko-pictures",
                ContentType = "application/json",
                ContentBody = uploadData,
                // either use a path provided in the argument or the default one for public Mewdeko, other/cmds.json
                Key = path ?? "other/cmds.json",
                CannedACL = S3CannedACL.PublicRead
            });
        }

        // also send the file, but indented one, to chat
        await using var rDataStream = new MemoryStream(Encoding.ASCII.GetBytes(readableData));
        await ctx.Channel.SendFileAsync(rDataStream, "cmds.json", GetText("commandlist_regen"))
            .ConfigureAwait(false);
    }

    [MewdekoCommand, Usage, Description, Aliases]
    public async Task Guide() => await ctx.Channel.SendConfirmAsync("You can find the website at https://mewdeko.tech");
}

public class CommandTextEqualityComparer : IEqualityComparer<CommandInfo>
{
    public bool Equals(CommandInfo? x, CommandInfo? y) => x.Aliases[0] == y.Aliases[0];

    public int GetHashCode(CommandInfo obj) => obj.Aliases[0].GetHashCode(StringComparison.InvariantCulture);
}

internal class CommandJsonObject
{
    public string[] Aliases { get; set; }
    public string Description { get; set; }
    public string[] Usage { get; set; }
    public string Submodule { get; set; }
    public string Module { get; set; }
    public List<string> Options { get; set; }
    public string[] Requirements { get; set; }
}