﻿using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Mewdeko.Common;
using Mewdeko.Common.Attributes;
using Mewdeko.Common.Extensions.Interactive.Entities.Page;
using Mewdeko.Modules.Administration.Services;
using Mewdeko.Modules.Music.Extensions;
using Mewdeko.Services.strings;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Color = SixLabors.ImageSharp.Color;

#nullable enable
namespace Mewdeko._Extensions;

public static class Extensions
{
    public static Regex UrlRegex = new(@"^(https?|ftp)://(?<path>[^\s/$.?#].[^\s]*)$", RegexOptions.Compiled);

    public static TOut[] Map<TIn, TOut>(this TIn[] arr, Func<TIn, TOut> f) => Array.ConvertAll(arr, x => f(x));

    public static Task<IUserMessage> EmbedAsync(this IMessageChannel channel, CREmbed crEmbed,
        bool sanitizeAll = false)
    {
        var plainText = sanitizeAll
            ? crEmbed.PlainText?.SanitizeAllMentions() ?? ""
            : crEmbed.PlainText?.SanitizeMentions() ?? "";

        return channel.SendMessageAsync(plainText, embed: crEmbed.IsEmbedValid ? crEmbed.ToEmbed().Build() : null);
    }

    public static EmbedAuthorBuilder WithMusicIcon(this EmbedAuthorBuilder eab) => eab.WithIconUrl("https://i.imgur.com/nhKS3PT.png");

    public static List<ulong> GetGuildIds(this DiscordSocketClient client) => client.Guilds.Select(x => x.Id).ToList();

    // ReSharper disable once InvalidXmlDocComment
    /// Generates a string in the format 00:mm:ss if timespan is less than 2m.
    /// </summary>
    /// <param name="span">Timespan to convert to string</param>
    /// <returns>Formatted duration string</returns>
    public static string ToPrettyStringHm(this TimeSpan span)
    {
        if (span < TimeSpan.FromMinutes(2))
            return $"{span:mm}m {span:ss}s";
        return $"{(int) span.TotalHours:D2}h {span:mm}m";
    }

    public static IList<AdvancedLavaTrack>? AddRange(this IList<AdvancedLavaTrack> list,
        IEnumerable<AdvancedLavaTrack> tracks)
    {
        foreach (var i in tracks) list.Add(i);

        return list;
    }

    public static DateTime GetDateTimeFromTimeSpan(TimeSpan span) => DateTime.Now.Subtract(span);

    public static bool TryGetUrlPath(this string input, out string path)
    {
        var match = UrlRegex.Match(input);
        if (match.Success)
        {
            path = match.Groups["path"].Value;
            return true;
        }

        path = string.Empty;
        return false;
    }

    public static IEmote ToIEmote(this string emojiStr) =>
        Emote.TryParse(emojiStr, out var maybeEmote)
            ? maybeEmote
            : new Emoji(emojiStr);

    // https://github.com/SixLabors/Samples/blob/master/ImageSharp/AvatarWithRoundedCorner/Program.cs
    public static IImageProcessingContext ApplyRoundedCorners(this IImageProcessingContext ctx, float cornerRadius)
    {
        var size = ctx.GetCurrentSize();
        var corners = BuildCorners(size.Width, size.Height, cornerRadius);

        ctx.SetGraphicsOptions(new GraphicsOptions
        {
            Antialias = true,
            AlphaCompositionMode =
                PixelAlphaCompositionMode
                    .DestOut // enforces that any part of this shape that has color is punched out of the background
        });

        foreach (var c in corners) ctx = ctx.Fill(Color.Red, c);
        return ctx;
    }

    private static IPathCollection BuildCorners(int imageWidth, int imageHeight, float cornerRadius)
    {
        // first create a square
        var rect = new RectangularPolygon(-0.5f, -0.5f, cornerRadius, cornerRadius);

        // then cut out of the square a circle so we are left with a corner
        var cornerTopLeft = rect.Clip(new EllipsePolygon(cornerRadius - 0.5f, cornerRadius - 0.5f, cornerRadius));

        // corner is now a corner shape positions top left
        //lets make 3 more positioned correctly, we can do that by translating the original around the center of the image

        var rightPos = imageWidth - cornerTopLeft.Bounds.Width + 1;
        var bottomPos = imageHeight - cornerTopLeft.Bounds.Height + 1;

        // move it across the width of the image - the width of the shape
        var cornerTopRight = cornerTopLeft.RotateDegree(90).Translate(rightPos, 0);
        var cornerBottomLeft = cornerTopLeft.RotateDegree(-90).Translate(0, bottomPos);
        var cornerBottomRight = cornerTopLeft.RotateDegree(180).Translate(rightPos, bottomPos);

        return new PathCollection(cornerTopLeft, cornerBottomLeft, cornerTopRight, cornerBottomRight);
    }

    /// <summary>
    ///     First 10 characters of teh bot token.
    /// </summary>
    public static string RedisKey(this IBotCredentials bc) => bc.Token.Substring(0, 10);

    public static async Task<string> ReplaceAsync(this Regex regex, string input,
        Func<Match, Task<string>> replacementFn)
    {
        var sb = new StringBuilder();
        var lastIndex = 0;

        foreach (Match match in regex.Matches(input))
        {
            sb.Append(input, lastIndex, match.Index - lastIndex)
                .Append(await replacementFn(match).ConfigureAwait(false));

            lastIndex = match.Index + match.Length;
        }

        sb.Append(input, lastIndex, input.Length - lastIndex);
        return sb.ToString();
    }

    public static void ThrowIfNull<T>(this T o, string name) where T : class
    {
        if (o == null)
            throw new ArgumentNullException(nameof(o));
    }

    public static bool IsAuthor(this IMessage msg, IDiscordClient client) => msg.Author?.Id == client.CurrentUser.Id;

    public static string RealSummary(this CommandInfo cmd, IBotStrings strings, ulong? guildId, string prefix) => string.Format(strings.GetCommandStrings(cmd.Name, guildId).Desc, prefix);

    public static string[] RealRemarksArr(this CommandInfo cmd, IBotStrings strings, ulong? guildId, string prefix) =>
        Array.ConvertAll(strings.GetCommandStrings(cmd.MethodName(), guildId).Args,
            arg => GetFullUsage(cmd.Name, arg, prefix));

    public static string GetCommandImage(this CommandInfo cmd, IBotStrings strings, ulong? guildId, string prefix) => strings.GetCommandStrings(cmd.MethodName(), guildId).Image;

    public static string MethodName(this CommandInfo cmd) =>
        ((MewdekoCommandAttribute) cmd.Attributes.FirstOrDefault(x => x is MewdekoCommandAttribute)!)
        ?.MethodName
        ?? cmd.Name;
    // public static string RealRemarks(this CommandInfo cmd, IBotStrings strings, string prefix)
    //     => string.Join('\n', cmd.RealRemarksArr(strings, prefix));

    public static string GetFullUsage(string commandName, string args, string prefix) => $"{prefix}{commandName} {string.Format(args, prefix)}";

    public static EmbedBuilder AddPaginatedFooter(this EmbedBuilder embed, int curPage, int? lastPage)
    {
        if (lastPage != null)
            return embed.WithFooter(efb => efb.WithText($"{curPage + 1} / {lastPage + 1}"));
        return embed.WithFooter(efb => efb.WithText(curPage.ToString()));
    }

    public static EmbedBuilder WithOkColor(this EmbedBuilder eb) => eb.WithColor(Services.Mewdeko.OkColor);

    public static EmbedBuilder WithErrorColor(this EmbedBuilder eb) => eb.WithColor(Services.Mewdeko.ErrorColor);

    public static PageBuilder WithOkColor(this PageBuilder eb) => eb.WithColor(Services.Mewdeko.OkColor);

    public static PageBuilder WithErrorColor(this PageBuilder eb) => eb.WithColor(Services.Mewdeko.ErrorColor);

    public static HttpClient AddFakeHeaders(this HttpClient http)
    {
        AddFakeHeaders(http.DefaultRequestHeaders);
        return http;
    }

    public static void AddFakeHeaders(this HttpHeaders dict)
    {
        dict.Clear();
        dict.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        dict.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 6.1) AppleWebKit/535.1 (KHTML, like Gecko) Chrome/14.0.835.202 Safari/535.1");
    }

    public static IMessage? DeleteAfter(this IUserMessage? msg, int seconds, LogCommandService? logService = null)
    {
        if (msg is null)
            return null;

        Task.Run(async () =>
        {
            await Task.Delay(seconds * 1000).ConfigureAwait(false);
            logService?.AddDeleteIgnore(msg.Id);
            try
            {
                await msg.DeleteAsync().ConfigureAwait(false);
            }
            catch
            {
                // ignored
            }
        });
        return msg;
    }
    public static IMessage? DeleteAfter(this IMessage? msg, int seconds, LogCommandService? logService = null)
    {
        if (msg is null)
            return null;

        Task.Run(async () =>
        {
            await Task.Delay(seconds * 1000).ConfigureAwait(false);
            logService?.AddDeleteIgnore(msg.Id);
            try
            {
                await msg.DeleteAsync().ConfigureAwait(false);
            }
            catch
            {
                // ignored
            }
        });
        return msg;
    }

    public static ModuleInfo GetTopLevelModule(this ModuleInfo module)
    {
        while (module.Parent != null) module = module.Parent;
        return module;
    }

    public static async Task<IEnumerable<IGuildUser>> GetMembersAsync(this IRole role) =>
        (await role.Guild.GetUsersAsync(CacheMode.CacheOnly).ConfigureAwait(false)).Where(u =>
            u.RoleIds.Contains(role.Id));

    /// <summary>
    ///     Adds fallback fonts to <see cref="TextOptions" />
    /// </summary>
    /// <param name="opts"><see cref="TextOptions" /> to which fallback fonts will be added to</param>
    /// <param name="fallback">List of fallback Font Families to add</param>
    /// <returns>The same <see cref="TextOptions" /> to allow chaining</returns>
    public static TextOptions WithFallbackFonts(this TextOptions opts, List<FontFamily> fallback)
    {
        foreach (var ff in fallback) opts.FallbackFonts.Add(ff);
        return opts;
    }

    /// <summary>
    ///     Adds fallback fonts to <see cref="TextGraphicsOptions" />
    /// </summary>
    /// <param name="opts"><see cref="TextGraphicsOptions" /> to which fallback fonts will be added to</param>
    /// <param name="fallback">List of fallback Font Families to add</param>
    /// <returns>The same <see cref="TextGraphicsOptions" /> to allow chaining</returns>
    public static TextGraphicsOptions WithFallbackFonts(this TextGraphicsOptions opts, List<FontFamily> fallback)
    {
        opts.TextOptions.WithFallbackFonts(fallback);
        return opts;
    }

    public static MemoryStream ToStream(this Image<Rgba32> img, IImageFormat? format = null)
    {
        var imageStream = new MemoryStream();
        if (format?.Name == "GIF")
            img.SaveAsGif(imageStream);
        else
            img.SaveAsPng(imageStream, new PngEncoder
            {
                ColorType = PngColorType.RgbWithAlpha,
                CompressionLevel = PngCompressionLevel.BestCompression
            });
        imageStream.Position = 0;
        return imageStream;
    }

    public static Stream ToStream(this IEnumerable<byte> bytes, bool canWrite = false)
    {
        var ms = new MemoryStream(bytes as byte[] ?? bytes.ToArray(), canWrite);
        ms.Seek(0, SeekOrigin.Begin);
        return ms;
    }

    public static IEnumerable<IRole> GetRoles(this IGuildUser user) => user.RoleIds.Select(r => user.Guild.GetRole(r)).Where(r => r != null);

    public static bool IsImage(this HttpResponseMessage msg) => IsImage(msg, out _);

    public static bool IsImage(this HttpResponseMessage msg, out string? mimeType)
    {
        if (msg.Content.Headers.ContentType != null) mimeType = msg.Content.Headers.ContentType.MediaType;
        mimeType = null;
        if (mimeType == "image/png"
            || mimeType == "image/jpeg"
            || mimeType == "image/gif")
            return true;
        return false;
    }

    public static long? GetImageSize(this HttpResponseMessage msg)
    {
        if (msg.Content.Headers.ContentLength == null) return null;

        return msg.Content.Headers.ContentLength / 1.MB();
    }


    public static IEnumerable<Type> LoadFrom(this IServiceCollection collection, Assembly assembly)
    {
        // list of all the types which are added with this method
        var addedTypes = new List<Type>();

        Type[] allTypes;
        try
        {
            // first, get all types in te assembly
            allTypes = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            Log.Error(ex, "Error loading assembly types");
            return Enumerable.Empty<Type>();
        }

        // all types which have INService implementation are services
        // which are supposed to be loaded with this method
        // ignore all interfaces and abstract classes
        var services = new Queue<Type>(allTypes
            .Where(x => x.GetInterfaces().Contains(typeof(INService))
                        && !x.GetTypeInfo().IsInterface && !x.GetTypeInfo().IsAbstract
#if GLOBAL_Mewdeko
                        && x.GetTypeInfo().GetCustomAttribute<NoPublicBotAttribute>() == null
#endif
            )
            .ToArray());

        // we will just return those types when we're done instantiating them
        addedTypes.AddRange(services);

        // get all interfaces which inherit from INService
        // as we need to also add a service for each one of interfaces
        // so that DI works for them too
        var interfaces = new HashSet<Type>(allTypes
            .Where(x => x.GetInterfaces().Contains(typeof(INService))
                        && x.GetTypeInfo().IsInterface));

        // keep instantiating until we've instantiated them all
        while (services.Count > 0)
        {
            var serviceType = services.Dequeue(); //get a type i need to add

            if (collection.FirstOrDefault(x => x.ServiceType == serviceType) !=
                null) // if that type is already added, skip
                continue;

            //also add the same type
            var interfaceType = interfaces.FirstOrDefault(x => serviceType.GetInterfaces().Contains(x));
            if (interfaceType != null)
            {
                addedTypes.Add(interfaceType);
                collection.AddSingleton(interfaceType, serviceType);
            }
            else
            {
                collection.AddSingleton(serviceType, serviceType);
            }
        }

        return addedTypes;
    }
}