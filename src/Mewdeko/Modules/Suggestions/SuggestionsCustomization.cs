using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Mewdeko._Extensions;
using Mewdeko.Common;
using Mewdeko.Common.Attributes;
using Mewdeko.Modules.Suggestions.Services;

namespace Mewdeko.Modules.Suggestions;

[Group]
public class SuggestionsCustomization : MewdekoModuleBase<SuggestionsService>
{
    public DiscordSocketClient Client;

    public SuggestionsCustomization(DiscordSocketClient client) => Client = client;

    [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild),
     UserPerm(GuildPermission.Administrator)]
    public async Task SuggestMessage([Remainder] string embed)
    {
        var e = SmartEmbed.TryParse(embed, out _, out _);
        if (embed == "-")
        {
            await Service.SetSuggestionMessage(ctx.Guild, embed);
            await ctx.Channel.SendConfirmAsync("Suggestions will now have the default look.");
            return;
        }

        if (!e || !embed.Contains("%suggest"))
        {
            await ctx.Channel.SendErrorAsync(
                "The embed code you provided cannot be used for suggestion messages!");
            return;
        }

        await Service.SetSuggestionMessage(ctx.Guild, embed);
        var ebe = SmartEmbed.TryParse(Service.GetSuggestionMessage(ctx.Guild), out _, out _);
        if (ebe is false)
        {
            await Service.SetSuggestionMessage(ctx.Guild, "-");
            await ctx.Channel.SendErrorAsync(
                "There was an error checking the embed, it may be invalid, so I set the suggest message back to default. Please dont hesitate to ask for embed help in the support server at https://discord.gg/6n3aa9Xapf.");
            return;
        }

        await ctx.Channel.SendConfirmAsync("Sucessfully updated suggestion message!");
    }

    [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild),
     UserPerm(GuildPermission.Administrator)]
    public async Task MinSuggestionLength(int length)
    {
        if (length >= 2048)
        {
            await ctx.Channel.SendErrorAsync(
                "Can't set this value because it means users will not be able to suggest anything!");
            return;
        }

        await Service.SetMinLength(ctx.Guild, length);
        await ctx.Channel.SendConfirmAsync($"Minimum length set to {length} characters!");
    }
    
    [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild),
     UserPerm(GuildPermission.Administrator)]
    public async Task MaxSuggestionLength(int length)
    {
        if (length <= 0)
        {
            await ctx.Channel.SendErrorAsync(
                "Cant set this value because it means users will not be able to suggest anything!");
            return;
        }

        await Service.SetMaxLength(ctx.Guild, length);
        await ctx.Channel.SendConfirmAsync($"Max length set to {length} characters!");
    }
    
    [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild),
     UserPerm(GuildPermission.Administrator)]
    public async Task AcceptMessage([Remainder] string embed)
    {
        var e = SmartEmbed.TryParse(embed, out _, out _);
        if (embed == "-")
        {
            await Service.SetAcceptMessage(ctx.Guild, embed);
            await ctx.Channel.SendConfirmAsync("Accepted Suggestions will now have the default look.");
            return;
        }

        if (!e || !embed.Contains("%suggest"))
        {
            await ctx.Channel.SendErrorAsync(
                "The embed code you provided cannot be used for accepted suggestion messages!");
            return;
        }

        await Service.SetAcceptMessage(ctx.Guild, embed);
        var ebe = SmartEmbed.TryParse(Service.GetAcceptMessage(ctx.Guild), out _, out _);
        if (ebe is false)
        {
            await Service.SetAcceptMessage(ctx.Guild, "-");
            await ctx.Channel.SendErrorAsync(
                "There was an error checking the embed, it may be invalid, so I set the accept message back to default. Please dont hesitate to ask for embed help in the support server at https://discord.gg/6n3aa9Xapf.");
            return;
        }

        await ctx.Channel.SendConfirmAsync("Sucessfully updated accepted suggestion message!");
    }

    [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild),
     UserPerm(GuildPermission.Administrator)]
    public async Task ImplementMessage([Remainder] string embed)
    {
        var e = SmartEmbed.TryParse(embed, out _, out _);
        if (embed == "-")
        {
            await Service.SetImplementMessage(ctx.Guild, embed);
            await ctx.Channel.SendConfirmAsync("Implemented Suggestions will now have the default look.");
            return;
        }

        if (!e || !embed.Contains("%suggest"))
        {
            await ctx.Channel.SendErrorAsync(
                "The embed code you provided cannot be used for implemented suggestion messages!");
            return;
        }

        await Service.SetImplementMessage(ctx.Guild, embed);
        var ebe = SmartEmbed.TryParse(Service.GetImplementMessage(ctx.Guild), out _, out _);
        if (ebe is false)
        {
            await Service.SetImplementMessage(ctx.Guild, "-");
            await ctx.Channel.SendErrorAsync(
                "There was an error checking the embed, it may be invalid, so I set the implemented message back to default. Please dont hesitate to ask for embed help in the support server at https://discord.gg/6n3aa9Xapf.");
            return;
        }

        await ctx.Channel.SendConfirmAsync("Sucessfully updated implemented suggestion message!");
    }

    [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild),
     UserPerm(GuildPermission.Administrator)]
    public async Task DenyMessage([Remainder] string embed)
    {
        var e = SmartEmbed.TryParse(embed, out _, out _);
        if (embed == "-")
        {
            await Service.SetDenyMessage(ctx.Guild, embed);
            await ctx.Channel.SendConfirmAsync("Denied Suggestions will now have the default look.");
            return;
        }

        if (!e || !embed.Contains("%suggest"))
        {
            await ctx.Channel.SendErrorAsync(
                "The embed code you provided cannot be used for denied suggestion messages!");
            return;
        }

        await Service.SetDenyMessage(ctx.Guild, embed);
        var ebe = SmartEmbed.TryParse(Service.GetDenyMessage(ctx.Guild), out _, out _);
        if (ebe is false)
        {
            await Service.SetDenyMessage(ctx.Guild, "-");
            await ctx.Channel.SendErrorAsync(
                "There was an error checking the embed, it may be invalid, so I set the deny message back to default. Please dont hesitate to ask for embed help in the support server at https://discord.gg/6n3aa9Xapf.");
            return;
        }

        await ctx.Channel.SendConfirmAsync("Sucessfully updated denied suggestion message!");
    }

    [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild),
     UserPerm(GuildPermission.Administrator)]
    public async Task ConsiderMessage([Remainder] string embed)
    {
        var e = SmartEmbed.TryParse(embed, out _, out _);
        if (embed == "-")
        {
            await Service.SetConsiderMessage(ctx.Guild, embed);
            await ctx.Channel.SendConfirmAsync("Considered Suggestions will now have the default look.");
            return;
        }

        if (!e || !embed.Contains("%suggest"))
        {
            await ctx.Channel.SendErrorAsync(
                "The embed code you provided cannot be used for considered suggestion messages!");
            return;
        }

        await Service.SetConsiderMessage(ctx.Guild, embed);
        var ebe = SmartEmbed.TryParse(Service.GetConsiderMessage(ctx.Guild), out _, out _);
        if (ebe is false)
        {
            await Service.SetConsiderMessage(ctx.Guild, "-");
            await ctx.Channel.SendErrorAsync(
                "There was an error checking the embed, it may be invalid, so I set the Consider message back to default. Please dont hesitate to ask for embed help in the support server at https://discord.gg/6n3aa9Xapf.");
            return;
        }

        await ctx.Channel.SendConfirmAsync("Sucessfully updated considered suggestion message!");
    }

    [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild),
     UserPerm(GuildPermission.Administrator)]
    public async Task SuggestMotes([Remainder] string? _ = null)
    {
        if (_ == null)
        {
            await ctx.Channel.SendErrorAsync(
                "You need to either provide emojis or say disable for this to work!");
            return;
        }

        if (_ != null && _.Contains("disable"))
        {
            await Service.SetSuggestionEmotes(ctx.Guild, "disable");
            await ctx.Channel.SendConfirmAsync("Disabled Custom Emotes for Suggestions");
            return;
        }

        if (_ != null && !_.Contains("disable") && ctx.Message.Tags.Where(t => t.Type == TagType.Emoji)
                .Select(x => (Emote) x.Value).Count() > 5)
        {
            await ctx.Channel.SendErrorAsync("You may only have up to 5 emotes for suggestions!");
            return;
        }

        if (!_.Contains("disable") && !ctx.Message.Tags.Where(t => t.Type == TagType.Emoji)
                .Select(x => (Emote) x.Value).Any())
        {
            await ctx.Channel.SendErrorAsync("You need to specify up to 5 emotes for this command to work!");
            return;
        }

        var emotes = ctx.Message.Tags.Where(t => t.Type == TagType.Emoji).Select(x => (Emote) x.Value);
        foreach (var emoji in emotes)
            if (!(await ctx.Guild.GetEmotesAsync()).Contains(emoji))
            {
                await ctx.Channel.SendMessageAsync(emoji.ToString());
                await ctx.Channel.SendErrorAsync(
                    "One or more emotes you provided is not in this server, please use only emotes in the server.");
                return;
            }

        var list = new List<string>();
        foreach (var emote in emotes) list.Add(emote.ToString());
        await Service.SetSuggestionEmotes(ctx.Guild, string.Join(",", list));
        await ctx.Channel.SendConfirmAsync($"Suggestions will now be reacted with {string.Join(",", list)}");
    }
}