using Amazon.S3;
using Amazon.S3.Model;
using Discord.Commands;
using Fergun.Interactive;
using Fergun.Interactive.Pagination;
using Mewdeko.Common.Attributes.TextCommands;
using Mewdeko.Modules.Help.Services;
using Mewdeko.Modules.Permissions.Services;
using Mewdeko.Services.Settings;
using Mewdeko.Services.strings;
using Newtonsoft.Json;
using Swan;
using System.IO;
using System.Text;

namespace Mewdeko.Modules.Help;

public class Help : MewdekoModuleBase<HelpService>
{
    private readonly BotConfigService _bss;
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
        _strings = strings;
        _lazyClientId = new AsyncLazy<ulong>(async () => (await client.GetApplicationInfoAsync()).Id);
    }

    public async Task<(string plainText, EmbedBuilder embed, ComponentBuilder? components)> GetHelpStringEmbed()
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

        if (SmartEmbed.TryParse(r.Replace(botSettings.HelpText), ctx.Guild?.Id, out var embed, out var plainText, out var components))
            return (plainText, embed, components);
        var eb = new EmbedBuilder().WithOkColor()
                                   .WithDescription(string.Format(botSettings.HelpText, clientId, Prefix));
        return (plainText, eb, null);
    }

    [Cmd, Aliases]
    public async Task SearchCommand(string commandname)
    {
        var cmds = _cmds.Commands.Distinct().Where(c => c.Name.Contains(commandname, StringComparison.InvariantCulture));
        if (!cmds.Any())
        {
            await ctx.Channel.SendErrorAsync(
                "That command wasn't found! Please retry your search with a different term.");
        }
        else
        {
            string cmdnames = null;
            string cmdremarks = null;
            foreach (var i in cmds)
            {
                cmdnames += $"\n{i.Name}";
                cmdremarks += $"\n{i.RealSummary(_strings, ctx.Guild.Id, Prefix).Truncate(50)}";
            }
            var eb = new EmbedBuilder()
                     .WithOkColor()
                     .AddField("Command", cmdnames, true)
                     .AddField("Description", cmdremarks, true);
            await ctx.Channel.SendMessageAsync(embed: eb.Build());
        }
    }

    [Cmd, Aliases]
    public async Task Modules()
    {
        var embed = Service.GetHelpEmbed(false, ctx.Guild, ctx.Channel, ctx.User);
        await HelpService.AddUser(ctx.Message, DateTime.UtcNow);
        await ctx.Channel.SendMessageAsync(embed: embed.Build(), components: Service.GetHelpComponents(ctx.Guild, ctx.User).Build());
    }

    [Cmd, Aliases]
    public async Task Donate() =>
        await ctx.Channel.SendConfirmAsync(
            "If you would like to support the project, here's how:\nKo-Fi: https://ko-fi.com/mewdeko\nI appreciate any donations as they will help improve Mewdeko for the better!");

    [Cmd, Aliases]
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
            .WithActionOnCancellation(ActionOnStop.DeleteMessage)
            .Build();

        await _interactive.SendPaginatorAsync(paginator, Context.Channel,
            TimeSpan.FromMinutes(60));

        async Task<PageBuilder> PageFactory(int page)
        {
            await Task.CompletedTask;
            var transformed = groups.Select(x => x.ElementAt(page).Where(x => !x.Attributes.Any(x => x is HelpDisabled)).Select(commandInfo =>
                    $"{(succ.Contains(commandInfo) ? "✅" : "❌")}{Prefix + commandInfo.Aliases[0],-15} {$"[{commandInfo.Aliases.Skip(1).FirstOrDefault()}]",-8}"))
                .FirstOrDefault();
            var last = groups.Select(x => x.Count()).FirstOrDefault();
            for (i = 0; i < last; i++)
            {
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
            }

            return new PageBuilder()
                .AddField(groups.Select(x => x.ElementAt(page).Key).FirstOrDefault(),
                    $"```css\n{string.Join("\n", transformed)}\n```")
                .WithDescription(
                    $"✅: You can use this command.\n❌: You cannot use this command.\nDo `{Prefix}h commandname` to see info on that command")
                .WithOkColor();
        }
    }

    [Cmd, Aliases, Priority(0)]
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

    [Cmd, Aliases, Priority(1)]
    public async Task H([Remainder] CommandInfo? com = null)
    {
        var channel = ctx.Channel;

        if (com == null)
        {
            await Modules();
            return;
        }

        var comp = new ComponentBuilder().WithButton(GetText("help_run_cmd"), $"runcmd.{com.Aliases[0]}", ButtonStyle.Success);
        var embed = Service.GetCommandHelp(com, ctx.Guild);
        await channel.SendMessageAsync(embed: embed.Build(), components: comp.Build()).ConfigureAwait(false);
    }

    [Cmd, Aliases, OwnerOnly]
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
                x => x.Distinct(commandInfo => commandInfo.Aliases[0])
                    .Select(com =>
                    {
                        com.Module.GetTopLevelModule();
                        List<string> optHelpStr = null!;
                        var opt = ((MewdekoOptionsAttribute)com.Attributes.FirstOrDefault(attribute =>
                            attribute is MewdekoOptionsAttribute))?.OptionType;
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
            var config = new AmazonS3Config { ServiceURL = serviceUrl };
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

    [Cmd, Aliases]
    public async Task Guide() => await ctx.Channel.SendConfirmAsync("You can find the website at https://mewdeko.tech");
    [Cmd, Aliases]
    public async Task Source() => await ctx.Channel.SendConfirmAsync("https://github.com/Sylveon76/Mewdeko");
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
