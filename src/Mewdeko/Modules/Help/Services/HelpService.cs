using CommandLine;
using Discord.Commands;
using Discord.Interactions;
using Mewdeko.Common.Attributes.TextCommands;
using Mewdeko.Common.ModuleBehaviors;
using Mewdeko.Modules.Administration.Services;
using Mewdeko.Modules.Permissions.Common;
using Mewdeko.Modules.Permissions.Services;
using Mewdeko.Services.Settings;
using Mewdeko.Services.strings;
using MoreLinq;
using System.Threading;

namespace Mewdeko.Modules.Help.Services;

public class HelpService : ILateExecutor, INService
{
    private readonly BotConfigService _bss;
    public static List<UMsg> UsrMsg = new();
    private readonly CommandHandler _ch;
    private readonly DiscordSocketClient _client;
    private readonly DiscordPermOverrideService _dpos;
    private readonly Mewdeko _bot;
    private readonly BlacklistService _blacklistService;
    private readonly IBotStrings _strings;
    private readonly CommandService _cmds;
    private readonly GlobalPermissionService _perms;
    private readonly PermissionService _nPerms;
    private readonly IDataCache _cache;
    private readonly InteractionService _interactionService;

    public HelpService(
        CommandHandler ch,
        IBotStrings strings,
        DiscordPermOverrideService dpos,
        BotConfigService bss,
        DiscordSocketClient client,
        Mewdeko bot,
        BlacklistService blacklistService,
        CommandService cmds,
        GlobalPermissionService perms,
        PermissionService nPerms,
        IDataCache cache,
        InteractionService interactionService)
    {
        _dpos = dpos;
        _strings = strings;
        _client = client;
        _bot = bot;
        _blacklistService = blacklistService;
        _cmds = cmds;
        _bss = bss;
        _ch = ch;
        _client.MessageReceived += HandlePing;
        _perms = perms;
        _nPerms = nPerms;
        _cache = cache;
        _interactionService = interactionService;
        _ = ClearHelp();
    }

    public ComponentBuilder GetHelpComponents(IGuild? guild, IUser user, bool descriptions = true)
    {
        var modules = _cmds.Commands.Select(x => x.Module).Where(x => !x.IsSubmodule).Distinct();
        var compBuilder = new ComponentBuilder();
        var selMenu = new SelectMenuBuilder().WithCustomId("helpselect");
        foreach (var i in modules.Where(x => !x.Attributes.Any(x => x is HelpDisabled)))
        {
            selMenu.Options.Add(new SelectMenuOptionBuilder().WithLabel(i.Name).WithDescription(GetText($"module_description_{i.Name.ToLower()}", guild)).WithValue(i.Name.ToLower()));
        }

        compBuilder.WithSelectMenu(selMenu);
        compBuilder.WithButton("Toggle Descriptions", $"toggle-descriptions:{descriptions},{user.Id}");
        return compBuilder;
    }

    public EmbedBuilder GetHelpEmbed(bool description, IGuild? guild, IMessageChannel channel, IUser user)
    {
        EmbedBuilder embed = new();
        embed.WithTitle($"{_bot.Client.CurrentUser} Help");
        embed.WithOkColor();
        embed.WithDescription(
            $"\nDo `{_ch.GetPrefix(guild)}help command` to see a description of a command you need more info on!\nDo `{_ch.GetPrefix(guild)}cmds category` to see the commands in that module.\n\n[Invite Me](https://discord.com/oauth2/authorize?client_id={_bot.Client.CurrentUser.Id}&scope=bot&permissions=66186303&scope=bot%20applications.commands)");
        var modules = _cmds.Commands.Select(x => x.Module).Where(x => !x.IsSubmodule && !x.Attributes.Any(x => x is HelpDisabled)).Distinct();
        var count = 0;
        if (description)
        {
            foreach (var mod in modules)
            {
                embed.AddField($"{CheckEnabled(guild?.Id, channel, user, mod.Name)} {mod.Name}", $">>> {GetModuleDescription(mod.Name, guild)}", true);
            }
        }
        else
        {
            foreach (var i in modules.Batch(modules.Count() / 2))
            {
                embed.AddField(count == 0 ? "Categories" : "_ _", string.Join("\n", i.Select(x => $"> {CheckEnabled(guild?.Id, channel, user, x.Name)} {Format.Bold(x.Name)}")), true);
                count++;
            }
        }

        return embed;
    }

    public string CheckEnabled(ulong? guildId, IMessageChannel channel, IUser user, string moduleName)
    {
        if (guildId is null)
            return "✅";
        var pc = _nPerms.GetCacheFor(guildId.Value);
        if (_perms.BlockedModules.Contains(moduleName.ToLower())) return "🌐❌";
        return !pc.Permissions.CheckSlashPermissions(moduleName, "none", user, channel, out _) ? "❌" : "✅";
    }

    public string GetModuleDescription(string module, IGuild? guild) => GetText($"module_description_{module.ToLower()}", guild);

    public record UMsg
    {
        public IUserMessage Msg { get; set; }
        public DateTime Time { get; set; }
    }

    public static Task AddUser(IUserMessage msg, DateTime time)
    {
        var tocheck = UsrMsg.Find(x => x.Msg == msg);
        if (tocheck is not null)
        {
            UsrMsg.Remove(tocheck);
            UsrMsg.Add(new UMsg { Msg = msg, Time = time });
            return Task.CompletedTask;
        }
        UsrMsg.Add(new UMsg { Msg = msg, Time = time });
        return Task.CompletedTask;
    }

    public static IUserMessage GetUserMessage(IUser user) =>
        UsrMsg.Find(x => x.Msg.Author == user)?.Msg;

    public static async Task ClearHelp()
    {
        var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync())
        {
            var tocheck = UsrMsg.Where(x => DateTime.UtcNow.Subtract(x.Time) >= TimeSpan.FromMinutes(5));
            if (tocheck.Any())
                UsrMsg.RemoveRange(tocheck);
        }
    }
    public Task LateExecute(DiscordSocketClient client, IGuild? guild, IUserMessage msg)
    {
        var settings = _bss.Data;
        if (guild != null) return Task.CompletedTask;
        if (string.IsNullOrWhiteSpace(settings.DmHelpText) || settings.DmHelpText == "-")
            return Task.CompletedTask;

        return SmartEmbed.TryParse(settings.DmHelpText, guild?.Id, out var embed, out var plainText, out var components)
            ? msg.Channel.SendMessageAsync(plainText, embed: embed.Build(), components:components.Build())
            : msg.Channel.SendMessageAsync(settings.DmHelpText);
    }

    private Task HandlePing(SocketMessage msg)
    {
        _ = Task.Run(async () =>
        {
            if (msg.Content == $"<@{_client.CurrentUser.Id}>" || msg.Content == $"<@!{_client.CurrentUser.Id}>")
            {
                if (msg.Channel is ITextChannel chan)
                {
                    var eb = new EmbedBuilder();
                    eb.WithOkColor();
                    eb.WithDescription(
                        $"Hi there! To see my command categories do `{_ch.GetPrefix(chan.Guild)}cmds`\nMy current Prefix is `{_ch.GetPrefix(chan.Guild)}`\n\n I hope you have a great day!");
                    eb.WithThumbnailUrl("https://cdn.discordapp.com/emojis/914307922287276052.gif");
                    eb.WithFooter(new EmbedFooterBuilder().WithText(_client.CurrentUser.Username).WithIconUrl(_client.CurrentUser.RealAvatarUrl().ToString()));
                    await chan.SendMessageAsync(embed: eb.Build());
                }
            }
        });
        return Task.CompletedTask;
    }

    public EmbedBuilder GetCommandHelp(CommandInfo com, IGuild guild)
    {
        if (com.Attributes.Any(x => x is HelpDisabled))
            return new EmbedBuilder().WithDescription("Help is disabled for this command.");
        var prefix = _ch.GetPrefix(guild);
        var a = com.MethodName();
        var potentialCommand = _interactionService.SlashCommands.FirstOrDefault(x => string.Equals(x.MethodName, com.MethodName(), StringComparison.CurrentCultureIgnoreCase));
        var str = $"**`{prefix + com.Aliases[0]}`**";
        var alias = com.Aliases.Skip(1).FirstOrDefault();
        if (alias != null)
            str += string.Format(" **/ `{0}`**", prefix + alias);
        var em = new EmbedBuilder().AddField(fb =>
            fb.WithName(str).WithValue($"{com.RealSummary(_strings, guild?.Id, prefix)}").WithIsInline(true));

        _dpos.TryGetOverrides(guild?.Id ?? 0, com.Name, out var overrides);
        var reqs = GetCommandRequirements(com, overrides);
        var botReqs = GetCommandBotRequirements(com);
        if (reqs.Length > 0)
            em.AddField("User Permissions", string.Join("\n", reqs));
        if (botReqs.Length > 0)
            em.AddField("Bot Permissions", string.Join("\n", botReqs));
        em.AddField("Slash Command", potentialCommand == null ? "`None`" : $"`/{potentialCommand.Module.SlashGroupName} {potentialCommand.Name}`");
        em.AddField(fb => fb.WithName(GetText("usage", guild)).WithValue(string.Join("\n",
                                Array.ConvertAll(com.RealRemarksArr(_strings, guild?.Id, prefix),
                                    arg => Format.Code(arg))))
                            .WithIsInline(false))
          .WithFooter($"Module: {com.Module.GetTopLevelModule().Name} || Submodule: {com.Module.Name.Replace("Commands", "")}")
          .WithColor(Mewdeko.OkColor);

        var opt = ((MewdekoOptionsAttribute)com.Attributes.FirstOrDefault(x => x is MewdekoOptionsAttribute))
            ?.OptionType;
        if (opt == null) return em;
        var hs = GetCommandOptionHelp(opt);
        if (!string.IsNullOrWhiteSpace(hs))
            em.AddField(GetText("options", guild), hs);

        return em;
    }

    public static string GetCommandOptionHelp(Type opt)
    {
        var strs = GetCommandOptionHelpList(opt);

        return string.Join("\n", strs);
    }

    public static List<string> GetCommandOptionHelpList(Type opt)
    {
        return opt.GetProperties()
                      .Select(x => Array.Find(x.GetCustomAttributes(true), a => a is OptionAttribute))
                      .Where(x => x != null).Cast<OptionAttribute>().Select(x =>
                      {
                          var toReturn = $"`--{x.LongName}`";

                          if (!string.IsNullOrWhiteSpace(x.ShortName))
                              toReturn += $" (`-{x.ShortName}`)";

                          toReturn += $"   {x.HelpText}  ";
                          return toReturn;
                      }).ToList();
    }

    public static string[] GetCommandRequirements(CommandInfo cmd, GuildPermission? overrides = null)
    {
        var toReturn = new List<string>();

        if (cmd.Preconditions.Any(x => x is OwnerOnlyAttribute))
            toReturn.Add("Bot Owner Only");

        var userPerm = (UserPermAttribute)cmd.Preconditions.FirstOrDefault(ca => ca is UserPermAttribute);

        var userPermString = string.Empty;
        if (userPerm is not null)
        {
            if (userPerm.UserPermissionAttribute.ChannelPermission is { } cPerm)
                userPermString = GetPreconditionString(cPerm);
            if (userPerm.UserPermissionAttribute.GuildPermission is { } gPerm)
                userPermString = GetPreconditionString(gPerm);
        }

        if (overrides is null)
        {
            if (!string.IsNullOrWhiteSpace(userPermString))
                toReturn.Add(userPermString);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(userPermString))
                toReturn.Add(Format.Strikethrough(userPermString));

            toReturn.Add(GetPreconditionString(overrides.Value));
        }

        return toReturn.ToArray();
    }

    public static string[] GetCommandBotRequirements(CommandInfo cmd)
    {
        var toReturn = new List<string>();

        if (cmd.Preconditions.Any(x => x is OwnerOnlyAttribute))
            toReturn.Add("Bot Owner Only");

        var botPerm = (BotPermAttribute)cmd.Preconditions.FirstOrDefault(ca => ca is BotPermAttribute);

        var botPermString = string.Empty;
        if (botPerm is not null)
        {
            if (botPerm.ChannelPermission is { } cPerm)
                botPermString = GetPreconditionString(cPerm);
            if (botPerm.GuildPermission is { } gPerm)
                botPermString = GetPreconditionString(gPerm);
        }

        if (!string.IsNullOrWhiteSpace(botPermString))
            toReturn.Add(botPermString);

        return toReturn.ToArray();
    }

    public static string GetPreconditionString(ChannelPermission perm) =>
        (perm + " Channel Permission").Replace("Guild", "Server", StringComparison.InvariantCulture);

    public static string GetPreconditionString(GuildPermission perm) =>
        (perm + " Server Permission").Replace("Guild", "Server", StringComparison.InvariantCulture);

    private string GetText(string text, IGuild? guild, params object[] replacements) =>
        _strings.GetText(text, guild?.Id, replacements);
}
