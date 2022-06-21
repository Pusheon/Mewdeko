﻿using Mewdeko.Common.Configs;
using Mewdeko.Common.PubSub;
using SixLabors.ImageSharp.PixelFormats;

namespace Mewdeko.Services.Settings;

/// <summary>
///     Settings service for bot-wide configuration.
/// </summary>
public sealed class BotConfigService : ConfigServiceBase<BotConfig>
{
    private const string FILE_PATH = "data/bot.yml";
    private static readonly TypedKey<BotConfig> _changeKey = new("config.bot.updated");

    public BotConfigService(IConfigSeria serializer, IPubSub pubSub)
        : base(FILE_PATH, serializer, pubSub, _changeKey)
    {
        AddParsedProp("color.ok", bs => bs.Color.Ok, Rgba32.TryParseHex, ConfigPrinters.Color);
        AddParsedProp("color.error", bs => bs.Color.Error, Rgba32.TryParseHex, ConfigPrinters.Color);
        AddParsedProp("color.pending", bs => bs.Color.Pending, Rgba32.TryParseHex, ConfigPrinters.Color);
        AddParsedProp("help.text", bs => bs.HelpText, ConfigParsers.String, ConfigPrinters.ToString);
        AddParsedProp("help.dmtext", bs => bs.DmHelpText, ConfigParsers.String, ConfigPrinters.ToString);
        AddParsedProp("console.type", bs => bs.ConsoleOutputType, Enum.TryParse, ConfigPrinters.ToString);
        AddParsedProp("locale", bs => bs.DefaultLocale, ConfigParsers.Culture, ConfigPrinters.Culture);
        AddParsedProp("prefix", bs => bs.Prefix, ConfigParsers.String, ConfigPrinters.ToString);
        AddParsedProp("commandlogchannel", bs => bs.CommandLogChannel, ulong.TryParse, ConfigPrinters.ToString);

        UpdateColors();
        UpdateCommandChannel();
    }

    public override string Name { get; } = "bot";

    private void UpdateColors()
    {
        var ok = data.Color.Ok;
        var error = data.Color.Error;
        Mewdeko.OkColor = new Color(ok.R, ok.G, ok.B);
        Mewdeko.ErrorColor = new Color(error.R, error.G, error.B);
    }

    private void UpdateCommandChannel() => CommandHandler.CommandLogChannelId = data.CommandLogChannel;

    protected override void OnStateUpdate()
    {
        UpdateColors();
        UpdateCommandChannel();
    }
}