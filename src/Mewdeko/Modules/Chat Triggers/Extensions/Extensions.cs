using AngleSharp;
using AngleSharp.Html.Dom;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Configuration = AngleSharp.Configuration;

namespace Mewdeko.Modules.Chat_Triggers.Extensions;

public static class Extensions
{
    private static readonly Regex _imgRegex = new("%(img|image):(?<tag>.*?)%", RegexOptions.Compiled);

    private static Dictionary<Regex, Func<Match, Task<string>>> RegexPlaceholders { get; } = new()
    {
        {
            _imgRegex, async match =>
            {
                var tag = match.Groups["tag"].ToString();
                if (string.IsNullOrWhiteSpace(tag))
                    return "";

                var fullQueryLink = $"http://imgur.com/search?q={tag}";
                var config = Configuration.Default.WithDefaultLoader();
                using var document =
                    await BrowsingContext.New(config).OpenAsync(fullQueryLink).ConfigureAwait(false);
                var elems = document.QuerySelectorAll("a.image-list-link").ToArray();

                if (elems.Length == 0)
                    return "";

                var img = elems.ElementAtOrDefault(new MewdekoRandom().Next(0, elems.Length))?.Children
                    ?.FirstOrDefault() as IHtmlImageElement;

                if (img?.Source == null)
                    return "";

                return $" {img.Source.Replace("b.", ".", StringComparison.InvariantCulture)} ";
            }
        }
    };

    private static string ResolveTriggerString(this string str, DiscordSocketClient client) => str.Replace("%bot.mention%", client.CurrentUser.Mention, StringComparison.Ordinal);

    private static async Task<string?> ResolveResponseStringAsync(this string? str, IUserMessage ctx,
        DiscordSocketClient client, string resolvedTrigger, bool containsAnywhere)
    {
        var substringIndex = resolvedTrigger.Length;
        if (containsAnywhere)
        {
            switch (ctx.Content.AsSpan().GetWordPosition(resolvedTrigger))
            {
                case WordPosition.Start:
                    substringIndex++;
                    break;
                case WordPosition.End:
                    substringIndex = ctx.Content.Length;
                    break;
                case WordPosition.Middle:
                    substringIndex += ctx.Content.IndexOf(resolvedTrigger, StringComparison.InvariantCulture);
                    break;
            }
        }

        var canMentionEveryone = (ctx.Author as IGuildUser)?.GuildPermissions.MentionEveryone ?? true;

        var rep = new ReplacementBuilder()
            .WithDefault(ctx.Author, ctx.Channel, (ctx.Channel as ITextChannel)?.Guild as SocketGuild, client)
            .WithOverride("%target%", () =>
                canMentionEveryone
                    ? ctx.Content[substringIndex..].Trim()
                    : ctx.Content[substringIndex..].Trim().SanitizeMentions(true))
            .Build();

        str = rep.Replace(str);
        foreach (var ph in RegexPlaceholders)
        {
            str = await ph.Key.ReplaceAsync(str, ph.Value).ConfigureAwait(false);
        }
        return str;
    }

    public static Task<string?> ResponseWithContextAsync(this Database.Models.ChatTriggers cr, IUserMessage ctx,
        DiscordSocketClient client, bool containsAnywhere) =>
        cr.Response.ResolveResponseStringAsync(ctx, client, cr.Trigger.ResolveTriggerString(client),
            containsAnywhere);

    public static async Task<IUserMessage> Send(this Database.Models.ChatTriggers ct, IUserMessage ctx,
        DiscordSocketClient client, bool sanitize)
    {
        var channel = ct.DmResponse
            ? await ctx.Author.CreateDMChannelAsync().ConfigureAwait(false)
            : ctx.Channel;

        if (SmartEmbed.TryParse(ct.Response, ct.GuildId, out var crembed, out var plainText, out var components))
        {
            var trigger = ct.Trigger.ResolveTriggerString(client);
            var substringIndex = trigger.Length;
            if (ct.ContainsAnywhere)
            {
                var pos = ctx.Content.AsSpan().GetWordPosition(trigger);
                if (pos == WordPosition.Start)
                    substringIndex++;
                else if (pos == WordPosition.End)
                    substringIndex = ctx.Content.Length;
                else if (pos == WordPosition.Middle)
                    substringIndex += ctx.Content.IndexOf(trigger, StringComparison.InvariantCulture);
            }

            var canMentionEveryone = (ctx.Author as IGuildUser)?.GuildPermissions.MentionEveryone ?? true;

            var rep = new ReplacementBuilder()
                .WithDefault(ctx.Author, ctx.Channel, (ctx.Channel as ITextChannel)?.Guild as SocketGuild, client)
                .WithOverride("%target%", () => canMentionEveryone
                    ? ctx.Content[substringIndex..].Trim()
                    : ctx.Content[substringIndex..].Trim().SanitizeMentions(true))
                .Build();

            SmartEmbed.TryParse(rep.Replace(ct.Response), ct.GuildId, out crembed, out plainText, out components);
            if (sanitize)
                plainText = plainText.SanitizeMentions();
            
            if (ct.CrosspostingChannelId != 0 && ct.GuildId is not null or 0)
                await client.GetGuild(ct.GuildId ?? 0).GetTextChannel(ct.CrosspostingChannelId)
                            .SendMessageAsync(plainText, embeds: crembed);
            else if (!ct.CrosspostingWebhookUrl.IsNullOrWhiteSpace())
            {
                try
                {
                    using var whClient = new DiscordWebhookClient(ct.CrosspostingWebhookUrl);
                    await whClient.SendMessageAsync(plainText,
                        embeds: crembed);
                }
                catch (TaskCanceledException) { /* ignored */ }
            }
            return await channel.SendMessageAsync(plainText, embeds: crembed, components:components?.Build()).ConfigureAwait(false);
        }

        var context = (await ct.ResponseWithContextAsync(ctx, client, ct.ContainsAnywhere).ConfigureAwait(false))
            .SanitizeMentions(sanitize);
        if (ct.CrosspostingChannelId != 0 && ct.GuildId is not null or 0)
            await client.GetGuild(ct.GuildId ?? 0).GetTextChannel(ct.CrosspostingChannelId).SendMessageAsync(context);
        else if (!ct.CrosspostingWebhookUrl.IsNullOrWhiteSpace())
        {
            try
            {
                using var whClient = new DiscordWebhookClient(ct.CrosspostingWebhookUrl);
                await whClient.SendMessageAsync(context);
            }
            catch (TaskCanceledException) { /* ignored */ }
        }
        return await channel.SendMessageAsync(context).ConfigureAwait(false);
    }
    
    public static async Task<IUserMessage> SendInteraction(this Database.Models.ChatTriggers ct, SocketInteraction inter,
        DiscordSocketClient client, bool sanitize, IUserMessage fakeMsg, bool ephemeral = false)
    {

        if (SmartEmbed.TryParse(ct.Response, ct.GuildId, out var crembed, out var plainText, out var components))
        {

            var rep = new ReplacementBuilder()
                      .WithDefault(inter.User, inter.Channel, (inter.Channel as ITextChannel)?.Guild as SocketGuild, client)
                      .WithOverride("%target%", () => inter switch
                      {
                          IMessageCommandInteraction mData => mData.Data.Message.Content.SanitizeAllMentions(),
                          IUserCommandInteraction uData => uData.Data.User.Mention,
                          _ => "%target%"
                      })
                      .Build();

            SmartEmbed.TryParse(rep.Replace(ct.Response), ct.GuildId, out crembed, out plainText, out components );
            if (sanitize)
                plainText = plainText.SanitizeMentions();
            if (ct.CrosspostingChannelId != 0 && ct.GuildId is not null or 0)
                await client.GetGuild(ct.GuildId ?? 0).GetTextChannel(ct.CrosspostingChannelId)
                            .SendMessageAsync(plainText, embeds: crembed, components: components?.Build());
            else if (!ct.CrosspostingWebhookUrl.IsNullOrWhiteSpace())
            {
                try
                {
                    using var whClient = new DiscordWebhookClient(ct.CrosspostingWebhookUrl);
                    await whClient.SendMessageAsync(plainText,
                        embeds: crembed);
                }
                catch (TaskCanceledException) { /* ignored */ }
            }
            await inter.RespondAsync(plainText, embeds: crembed, ephemeral:ephemeral, components:components?.Build()).ConfigureAwait(false);
            return await inter.GetOriginalResponseAsync().ConfigureAwait(false);
        }

        var context = (await ct.ResponseWithContextAsync(fakeMsg, client, ct.ContainsAnywhere).ConfigureAwait(false))
            .SanitizeMentions(sanitize);
        if (ct.CrosspostingChannelId != 0 && ct.GuildId is not null or 0)
            await client.GetGuild(ct.GuildId ?? 0).GetTextChannel(ct.CrosspostingChannelId).SendMessageAsync(context);
        else if (!ct.CrosspostingWebhookUrl.IsNullOrWhiteSpace())
        {
            try
            {
                using var whClient = new DiscordWebhookClient(ct.CrosspostingWebhookUrl);
                await whClient.SendMessageAsync(context);
            }
            catch (TaskCanceledException) { /* ignored */ }
        }

        await inter.RespondAsync(context, ephemeral:ephemeral).ConfigureAwait(false);
        return await inter.GetOriginalResponseAsync().ConfigureAwait(false);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static WordPosition GetWordPosition(this ReadOnlySpan<char> str, in ReadOnlySpan<char> word)
    {
        var wordIndex = str.IndexOf(word, StringComparison.OrdinalIgnoreCase);
        switch (wordIndex)
        {
            case -1:
                return WordPosition.None;
            case 0:
                {
                    if (word.Length < str.Length && str.IsValidWordDivider(word.Length))
                        return WordPosition.Start;
                    break;
                }
            default:
                {
                    if (wordIndex + word.Length == str.Length)
                    {
                        if (str.IsValidWordDivider(wordIndex - 1))
                            return WordPosition.End;
                    }
                    else if (str.IsValidWordDivider(wordIndex - 1) && str.IsValidWordDivider(wordIndex + word.Length))
                    {
                        return WordPosition.Middle;
                    }

                    break;
                }
        }

        return WordPosition.None;
    }

    private static bool IsValidWordDivider(this in ReadOnlySpan<char> str, int index)
    {
        switch (str[index])
        {
            case >= 'a' and <= 'z':
            case >= 'A' and <= 'Z':
            case >= '1' and <= '9':
                return false;
            default:
                return true;
        }
    }
}

public enum WordPosition
{
    None,
    Start,
    Middle,
    End
}