using CommandLine;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Mewdeko._Extensions;
using Mewdeko.Common;
using Mewdeko.Common.Attributes;
using Mewdeko.Common.ModuleBehaviors;
using Mewdeko.Modules.Administration.Services;
using Mewdeko.Services.Settings;
using Mewdeko.Services.strings;
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
    private readonly IBotStrings _strings;
    public readonly ComponentBuilder Builder;
    
    public HelpService(
        CommandHandler ch,
        IBotStrings strings,
        DiscordPermOverrideService dpos,
        BotConfigService bss,
        DiscordSocketClient client,
        Mewdeko bot)
    {
        _client = client;
        _bot = bot;
        _ch = ch;
        _strings = strings;
        _dpos = dpos;
        _bss = bss;
        _client.MessageReceived += HandlePing;
        Builder = new ComponentBuilder().WithSelectMenu("helpselect",
            new List<SelectMenuOptionBuilder>
            {
                new SelectMenuOptionBuilder().WithLabel("Administration").WithDescription("Shows administration commands.").WithValue("admin"),
                new SelectMenuOptionBuilder().WithLabel("Afk").WithDescription("Shows AFK Commands").WithValue("afk"),
                new SelectMenuOptionBuilder().WithLabel("ChatTriggers").WithDescription("Shows ChatTriggers commands").WithValue("chattriggers"),
                new SelectMenuOptionBuilder().WithLabel("Confessions").WithDescription("Shows Confessions Commands").WithValue("confessions"),
                new SelectMenuOptionBuilder().WithLabel("Games").WithDescription("Shows Games Commands").WithValue("games"),
                new SelectMenuOptionBuilder().WithLabel("Gambling").WithDescription("Shows Gambling Commands").WithValue("gambling"),
                new SelectMenuOptionBuilder().WithLabel("Giveaways").WithDescription("Shows Giveaways Commands").WithValue("giveaways"),
                new SelectMenuOptionBuilder().WithLabel("Help").WithDescription("Shows Help Commands").WithValue("help"),
                new SelectMenuOptionBuilder().WithLabel("Highlights").WithDescription("Shows Highlights Commands").WithValue("highlights"),
                new SelectMenuOptionBuilder().WithLabel("Moderation").WithDescription("Shows Moderation Commands").WithValue("mod"),
                new SelectMenuOptionBuilder().WithLabel("MultiGreets").WithDescription("Shows MultiGreet Commands").WithValue("multigreets"),
                new SelectMenuOptionBuilder().WithLabel("Permissions").WithDescription("Shows Permissions Commands").WithValue("permissions"),
                new SelectMenuOptionBuilder().WithLabel("RoleGreets").WithDescription("Shows RoleGreets commands").WithValue("rolegreets"),
                new SelectMenuOptionBuilder().WithLabel("Searches").WithDescription("Shows Searches Commands").WithValue("searches"),
                new SelectMenuOptionBuilder().WithLabel("Server Management").WithDescription("Shows Server Management Commands").WithValue("server"),
                new SelectMenuOptionBuilder().WithLabel("Starboard").WithDescription("Shows Starboard Commands").WithValue("starboard"),
                new SelectMenuOptionBuilder().WithLabel("Suggestions").WithDescription("Shows Suggestions Commands").WithValue("suggestions"),
                new SelectMenuOptionBuilder().WithLabel("Utility").WithDescription("Shows Utility Commands").WithValue("utility"),
                new SelectMenuOptionBuilder().WithLabel("Xp").WithDescription("Shows Xp Commands").WithValue("xp")
            });
        _ = ClearHelp();
    }
    
    public record UMsg
    {
        public IUserMessage Msg { get; set; }
        public DateTime Time { get; set; }
    }

    public Task AddUser(IUserMessage msg, DateTime time)
    {
        var tocheck = UsrMsg.FirstOrDefault(x => x.Msg == msg);
        if (tocheck is not null)
        {
            UsrMsg.Remove(tocheck);
            UsrMsg.Add(new UMsg{Msg = msg, Time = time});
            return Task.CompletedTask;
        }
        UsrMsg.Add(new UMsg{Msg = msg, Time = time});
        return Task.CompletedTask;
    }

    public IUserMessage GetUserMessage(IUser user) => 
        UsrMsg.FirstOrDefault(x => x.Msg.Author == user)?.Msg ?? null;

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

        return SmartEmbed.TryParse(settings.DmHelpText, out var embed, out var plainText)
            ? msg.Channel.EmbedAsync(embed, plainText)
            : msg.Channel.SendMessageAsync(settings.DmHelpText);

    }


    private async Task HandlePing(SocketMessage msg)
    {
        if (msg.Content == $"<@{_client.CurrentUser.Id}>" || msg.Content == $"<@!{_client.CurrentUser.Id}>")
            if (msg.Channel is ITextChannel chan)
            {
                var eb = new EmbedBuilder();
                eb.WithOkColor();
                eb.WithDescription(
                    $"Hi there! To see my command categories do `{_ch.GetPrefix(chan.Guild)}cmds`\nMy current Prefix is `{_ch.GetPrefix(chan.Guild)}`\nIf you need help using the bot feel free to join the [Support Server](https://discord.gg/6n3aa9Xapf)!\n**Please support me! While this bot is free it's not free to run! https://ko-fi.com/mewdeko**\n\n I hope you have a great day!");
                eb.WithThumbnailUrl("https://cdn.discordapp.com/emojis/914307922287276052.gif");
                eb.WithFooter(new EmbedFooterBuilder().WithText(_client.CurrentUser.Username)
                                                      .WithIconUrl(_client.CurrentUser.RealAvatarUrl().ToString()));
                await chan.SendMessageAsync(embed: eb.Build());
            }
    }

    public EmbedBuilder GetCommandHelp(CommandInfo com, IGuild guild)
    {
        var prefix = _ch.GetPrefix(guild);

        var str = string.Format("**`{0}`**", prefix + com.Aliases.First());
        var alias = com.Aliases.Skip(1).FirstOrDefault();
        if (alias != null)
            str += string.Format(" **/ `{0}`**", prefix + alias);
        var em = new EmbedBuilder().AddField(fb =>
            fb.WithName(str).WithValue($"{com.RealSummary(_strings, guild?.Id, prefix)}").WithIsInline(true));

        _dpos.TryGetOverrides(guild?.Id ?? 0, com.Name, out var overrides);
        var reqs = GetCommandRequirements(com, overrides);
        var botReqs = GetCommandBotRequirements(com);
        if (reqs.Any())
            em.AddField("User Permissions", string.Join("\n", reqs));
        if (botReqs.Any())
            em.AddField("Bot Permissions", string.Join("\n", botReqs));

        em.AddField(fb => fb.WithName(GetText("usage", guild)).WithValue(string.Join("\n",
                                Array.ConvertAll(com.RealRemarksArr(_strings, guild?.Id, prefix),
                                    arg => Format.Code(arg))))
                            .WithIsInline(false))
          .WithFooter($"Module: {com.Module.GetTopLevelModule().Name} || Submodule: {com.Module.Name.Replace("Commands", "")}")
          .WithColor(Mewdeko.OkColor);

        var opt = ((MewdekoOptionsAttribute)com.Attributes.FirstOrDefault(x => x is MewdekoOptionsAttribute))
            ?.OptionType;
        if (opt != null)
        {
            var hs = GetCommandOptionHelp(opt);
            if (!string.IsNullOrWhiteSpace(hs))
                em.AddField(GetText("options", guild), hs);
        }

        return em;
    }

    public static string GetCommandOptionHelp(Type opt)
    {
        var strs = GetCommandOptionHelpList(opt);

        return string.Join("\n", strs);
    }

    public static List<string> GetCommandOptionHelpList(Type opt)
    {
        var strs = opt.GetProperties()
                      .Select(x => x.GetCustomAttributes(true).FirstOrDefault(a => a is OptionAttribute))
                      .Where(x => x != null).Cast<OptionAttribute>().Select(x =>
                      {
                          var toReturn = $"`--{x.LongName}`";

                          if (!string.IsNullOrWhiteSpace(x.ShortName))
                              toReturn += $" (`-{x.ShortName}`)";

                          toReturn += $"   {x.HelpText}  ";
                          return toReturn;
                      }).ToList();

        return strs;
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