namespace Mewdeko.Extensions;

public static class MessageExtensions
{
    public static string GetJumpLink(this IMessage message)
        => $"https://discord.com/channels/{(message.Channel is ITextChannel channel ? channel.GuildId : "@me")}/{message.Channel.Id}/{message.Id}";

    public static void ReplyError(this IUserMessage message, string content)
    {
        var eb = new EmbedBuilder().WithErrorColor().WithDescription(content);
        message.ReplyAsync(embed: eb.Build());
    }

    // public static SerializedEmbed GetJsonSource(this IMessage message)
    // {
    //     var eb = new SerializedEmbed1();
    //     if (message.Embeds.FirstOrDefault() == null) return new SerializedEmbed() { SerializedEmbed1 = eb, Content = message.Content };
    //     var embed = message.Embeds.FirstOrDefault();
    //     if (embed.Fields.Any())
    //     {
    //         foreach (var i in embed.Fields)
    //         {
    //             Field field = null;
    //             field.Inline = i.Inline;
    //             field.Name = i.Name;
    //             field.Value = i.Value;
    //             eb.Fields.Add(field);
    //         }
    //     }
    //     
    //     eb.Description = embed.Description;
    //     eb.Color = embed.Color.Value.RawValue;
    //     if (embed.Footer.HasValue)
    //     {
    //         eb.Footer = new Footer { IconUrl = embed.Footer.Value.IconUrl, Text = embed.Footer.Value.Text };
    //     }
    //
    //     if (embed.Author.HasValue)
    //     {
    //         eb.Author = new Author { IconUrl = embed.Author.Value.IconUrl.UnescapeUnicodeCodePoints(), Name = embed.Author.Value.Name, Url = embed.Author.Value.Url };
    //     }
    //
    //     if (embed.Image.HasValue)
    //     {
    //         eb.Image = new Image { Url = embed.Image.Value.Url  };
    //     }
    //
    //     if (embed.Thumbnail.HasValue)
    //     {
    //         eb.Thumbnail = new Thumbnail { Url = embed.Thumbnail.Value.Url };
    //     }
    //     eb.Title = embed.Title;
    //
    //     return new SerializedEmbed() { SerializedEmbed1 = eb, Content = message.Content };
    // }
}
