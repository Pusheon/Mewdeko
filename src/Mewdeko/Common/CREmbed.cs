using Newtonsoft.Json;

namespace Mewdeko.Common;

public class CrEmbed
{
    public CrEmbedAuthor? Author { get; set; }
    public string? PlainText { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? Url { get; set; }
    public CrEmbedFooter? Footer { get; set; }
    public string? Thumbnail { get; set; }
    public string? Image { get; set; }
    public CrEmbedField[]? Fields { get; set; }
    public uint Color { get; set; } = 7458112;
    public CrEmbedButton[]? Buttons { get; set; }

    public bool IsValid =>
        IsEmbedValid || !string.IsNullOrWhiteSpace(PlainText);

    public bool IsEmbedValid =>
        !string.IsNullOrWhiteSpace(Title) ||
        !string.IsNullOrWhiteSpace(Description) ||
        !string.IsNullOrWhiteSpace(Url) ||
        !string.IsNullOrWhiteSpace(Thumbnail) ||
        !string.IsNullOrWhiteSpace(Image) ||
        (Footer is not null && (!string.IsNullOrWhiteSpace(Footer.Text) || !string.IsNullOrWhiteSpace(Footer.IconUrl))) ||
        Fields is { Length: > 0 };

    public EmbedBuilder ToEmbed()
    {
        var embed = new EmbedBuilder();

        if (!string.IsNullOrWhiteSpace(Title))
            embed.WithTitle(Title);
        if (!string.IsNullOrWhiteSpace(Description))
            embed.WithDescription(Description);
        if (Url != null && Uri.IsWellFormedUriString(Url, UriKind.Absolute))
            embed.WithUrl(Url);
        embed.WithColor(new Color(Color));
        if (Footer != null)
        {
            embed.WithFooter(efb =>
            {
                efb.WithText(Footer.Text);
                if (Uri.IsWellFormedUriString(Footer.IconUrl, UriKind.Absolute))
                    efb.WithIconUrl(Footer.IconUrl);
            });
        }

        if (Thumbnail != null && Uri.IsWellFormedUriString(Thumbnail, UriKind.Absolute))
            embed.WithThumbnailUrl(Thumbnail);
        if (Image != null && Uri.IsWellFormedUriString(Image, UriKind.Absolute))
            embed.WithImageUrl(Image);
        if (Author != null && !string.IsNullOrWhiteSpace(Author.Name))
        {
            if (!Uri.IsWellFormedUriString(Author.IconUrl, UriKind.Absolute))
                Author.IconUrl = null;
            if (!Uri.IsWellFormedUriString(Author.Url, UriKind.Absolute))
                Author.Url = null;

            embed.WithAuthor(Author.Name, Author.IconUrl, Author.Url);
        }

        if (Fields == null) return embed;
        {
            foreach (var f in Fields)
            {
                if (!string.IsNullOrWhiteSpace(f.Name) && !string.IsNullOrWhiteSpace(f.Value))
                    embed.AddField(efb => efb.WithName(f.Name).WithValue(f.Value).WithIsInline(f.Inline));
            }
        }

        return embed;
    }

    public ComponentBuilder GetComponents(ulong? guildId)
    {
        var cb = new ComponentBuilder();

        Buttons?.Select((x, y) => (Triggers: x, Pos: y))
                .ForEach(x => cb.WithButton(GetButton(x.Triggers, x.Pos, guildId ?? 0)));

        return cb;
    }

    public static ButtonBuilder GetButton(CrEmbedButton btn, int pos, ulong guildId)
    {
        var bb = new ButtonBuilder();
        if (btn.Url.IsNullOrWhiteSpace() && btn.Id == 0)
            bb.WithDisabled(true).WithLabel("Buttons must have a url or id").WithStyle(ButtonStyle.Danger).WithCustomId(pos.ToString());
        else if (!btn.Url.IsNullOrWhiteSpace() && btn.Id != 0)
            bb.WithDisabled(true).WithLabel("Buttons cannot have both a url and id").WithStyle(ButtonStyle.Danger).WithCustomId(pos.ToString());
        else if (btn.Url.IsNullOrWhiteSpace() && btn.Style == ButtonStyle.Link)
            bb.WithDisabled(true).WithLabel("Button styles must be 1, 2, 3, or 4").WithStyle(ButtonStyle.Danger).WithCustomId(pos.ToString());
        else if (btn.DisplayName.IsNullOrWhiteSpace())
            bb.WithDisabled(true).WithLabel("Buttons must have a display name").WithStyle(ButtonStyle.Danger).WithCustomId(pos.ToString());
        else if (!btn.Url.IsNullOrWhiteSpace() && !btn.Url.StartsWith("https://")&& !btn.Url.StartsWith("http://")&& !btn.Url.StartsWith("discord://"))
            bb.WithDisabled(true).WithLabel("Buttons with a url must have a https://, https://, or discord:// link").WithStyle(ButtonStyle.Danger).WithCustomId(pos.ToString());
        else if (!btn.Url.IsNullOrWhiteSpace())
            bb.WithLabel(btn.DisplayName).WithStyle(ButtonStyle.Link).WithUrl(btn.Url);
        else
            bb.WithLabel(btn.DisplayName).WithStyle(btn.Style).WithCustomId($"trigger.{btn.Id}.runin.{guildId}${pos}");
        return bb;
    }
}

public class CrEmbedField
{
    public string Name { get; set; }
    public string Value { get; set; }
    public bool Inline { get; set; }
}

public class CrEmbedFooter
{
    public string Text { get; set; }
    public string IconUrl { get; set; }

    [JsonProperty("icon_url")]
    private string IconUrl1
    {
        set => IconUrl = value;
    }
}

public class CrEmbedAuthor
{
    public string Name { get; set; }
    public string IconUrl { get; set; }

    [JsonProperty("icon_url")]
    private string IconUrl1
    {
        set => IconUrl = value;
    }

    public string Url { get; set; }
}

public class CrEmbedButton
{
    public string DisplayName { get; set; }
    public int Id { get; set; }
    public ButtonStyle Style { get; set; } = ButtonStyle.Primary;
    public string Url { get; set; }
}