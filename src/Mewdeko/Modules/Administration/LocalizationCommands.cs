using System.Globalization;
using Discord;
using Discord.Commands;
using Mewdeko._Extensions;
using Mewdeko.Common;
using Mewdeko.Common.Attributes;

namespace Mewdeko.Modules.Administration;

public partial class Administration
{
    [Group]
    public class LocalizationCommands : MewdekoSubmodule
    {
        private static readonly IReadOnlyDictionary<string, string> _supportedLocales =
            new Dictionary<string, string>
            {
                {"ar", "العربية"},
                {"zh-TW", "繁體中文, 台灣"},
                {"zh-CN", "简体中文, 中华人民共和国"},
                {"nl-NL", "Nederlands, Nederland"},
                {"en-US", "English, United States"},
                {"fr-FR", "Français, France"},
                {"cs-CZ", "Čeština, Česká republika"},
                {"da-DK", "Dansk, Danmark"},
                {"de-DE", "Deutsch, Deutschland"},
                {"he-IL", "עברית, ישראל"},
                {"hu-HU", "Magyar, Magyarország"},
                {"id-ID", "Bahasa Indonesia, Indonesia"},
                {"it-IT", "Italiano, Italia"},
                {"ja-JP", "日本語, 日本"},
                {"ko-KR", "한국어, 대한민국"},
                {"hi-IN", "Hindi"},
                {"nb-NO", "Norsk, Norge"},
                {"pl-PL", "Polski, Polska"},
                {"pt-BR", "Português Brasileiro, Brasil"},
                {"owo", "*pounces"},
                {"ro-RO", "Română, România"},
                {"ru-RU", "Русский, Россия"},
                {"sr-Cyrl-RS", "Српски, Србија"},
                {"es-ES", "Español, España"},
                {"sv-SE", "Svenska, Sverige"},
                {"tr-TR", "Türkçe, Türkiye"},
                {"ts-TS", "Tsundere, You Baka"},
                {"uk-UA", "Українська, Україна"}
            };

        [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild),
         UserPerm(GuildPermission.Administrator), Priority(1)]
        public async Task LanguageSet(string name)
        {
            try
            {
                CultureInfo ci;
                if (name.Trim().ToLowerInvariant() == "default")
                {
                    Localization.RemoveGuildCulture(ctx.Guild);
                    ci = Localization.DefaultCultureInfo;
                }
                else
                {
                    ci = new CultureInfo(name);
                    Localization.SetGuildCulture(ctx.Guild, ci);
                }

                await ReplyConfirmLocalizedAsync("lang_set", Format.Bold(ci.ToString()), Format.Bold(ci.NativeName))
                    .ConfigureAwait(false);
            }
            catch (Exception)
            {
                await ReplyErrorLocalizedAsync("lang_set_fail").ConfigureAwait(false);
            }
        }

        [MewdekoCommand, Usage, Description, Aliases]
        public async Task LanguageSetDefault()
        {
            var cul = Localization.DefaultCultureInfo;
            await ReplyConfirmLocalizedAsync("lang_set_bot_show", cul, cul.NativeName).ConfigureAwait(false);
        }

        [MewdekoCommand, Usage, Description, Aliases, OwnerOnly]
        public async Task LanguageSetDefault(string name)
        {
            try
            {
                CultureInfo ci;
                if (name.Trim().ToLowerInvariant() == "default")
                {
                    Localization.ResetDefaultCulture();
                    ci = Localization.DefaultCultureInfo;
                }
                else
                {
                    ci = new CultureInfo(name);
                    Localization.SetDefaultCulture(ci);
                }

                await ReplyConfirmLocalizedAsync("lang_set_bot", Format.Bold(ci.ToString()),
                    Format.Bold(ci.NativeName)).ConfigureAwait(false);
            }
            catch (Exception)
            {
                await ReplyErrorLocalizedAsync("lang_set_fail").ConfigureAwait(false);
            }
        }

        [MewdekoCommand, Usage, Description, Aliases]
        public async Task LanguagesList() =>
            await ctx.Channel.EmbedAsync(new EmbedBuilder().WithOkColor()
                                                           .WithTitle(GetText("lang_list"))
                                                           .WithDescription(string.Join("\n",
                                                               _supportedLocales.Select(x => $"{Format.Code(x.Key),-10} => {x.Value}")))).ConfigureAwait(false);
    }
}
/* list of language codes for reference. 
 * taken from https://github.com/dotnet/coreclr/blob/ee5862c6a257e60e263537d975ab6c513179d47f/src/mscorlib/src/System/Globalization/CultureData.cs#L192
            { "029", "en-029" },
            { "AE",  "ar-AE" },
            { "AF",  "prs-AF" },
            { "AL",  "sq-AL" },
            { "AM",  "hy-AM" },
            { "AR",  "es-AR" },
            { "AT",  "de-AT" },
            { "AU",  "en-AU" },
            { "AZ",  "az-Cyrl-AZ" },
            { "BA",  "bs-Latn-BA" },
            { "BD",  "bn-BD" },
            { "BE",  "nl-BE" },
            { "BG",  "bg-BG" },
            { "BH",  "ar-BH" },
            { "BN",  "ms-BN" },
            { "BO",  "es-BO" },
            { "BR",  "pt-BR" },
            { "BY",  "be-BY" },
            { "BZ",  "en-BZ" },
            { "CA",  "en-CA" },
            { "CH",  "it-CH" },
            { "CL",  "es-CL" },
            { "CN",  "zh-CN" },
            { "CO",  "es-CO" },
            { "CR",  "es-CR" },
            { "CS",  "sr-Cyrl-CS" },
            { "CZ",  "cs-CZ" },
            { "DE",  "de-DE" },
            { "DK",  "da-DK" },
            { "DO",  "es-DO" },
            { "DZ",  "ar-DZ" },
            { "EC",  "es-EC" },
            { "EE",  "et-EE" },
            { "EG",  "ar-EG" },
            { "ES",  "es-ES" },
            { "ET",  "am-ET" },
            { "FI",  "fi-FI" },
            { "FO",  "fo-FO" },
            { "FR",  "fr-FR" },
            { "GB",  "en-GB" },
            { "GE",  "ka-GE" },
            { "GL",  "kl-GL" },
            { "GR",  "el-GR" },
            { "GT",  "es-GT" },
            { "HK",  "zh-HK" },
            { "HN",  "es-HN" },
            { "HR",  "hr-HR" },
            { "HU",  "hu-HU" },
            { "ID",  "id-ID" },
            { "IE",  "en-IE" },
            { "IL",  "he-IL" },
            { "IN",  "hi-IN" },
            { "IQ",  "ar-IQ" },
            { "IR",  "fa-IR" },
            { "IS",  "is-IS" },
            { "IT",  "it-IT" },
            { "IV",  "" },
            { "JM",  "en-JM" },
            { "JO",  "ar-JO" },
            { "JP",  "ja-JP" },
            { "KE",  "sw-KE" },
            { "KG",  "ky-KG" },
            { "KH",  "km-KH" },
            { "KR",  "ko-KR" },
            { "KW",  "ar-KW" },
            { "KZ",  "kk-KZ" },
            { "LA",  "lo-LA" },
            { "LB",  "ar-LB" },
            { "LI",  "de-LI" },
            { "LK",  "si-LK" },
            { "LT",  "lt-LT" },
            { "LU",  "lb-LU" },
            { "LV",  "lv-LV" },
            { "LY",  "ar-LY" },
            { "MA",  "ar-MA" },
            { "MC",  "fr-MC" },
            { "ME",  "sr-Latn-ME" },
            { "MK",  "mk-MK" },
            { "MN",  "mn-MN" },
            { "MO",  "zh-MO" },
            { "MT",  "mt-MT" },
            { "MV",  "dv-MV" },
            { "MX",  "es-MX" },
            { "MY",  "ms-MY" },
            { "NG",  "ig-NG" },
            { "NI",  "es-NI" },
            { "NL",  "nl-NL" },
            { "NO",  "nn-NO" },
            { "NP",  "ne-NP" },
            { "NZ",  "en-NZ" },
            { "OM",  "ar-OM" },
            { "PA",  "es-PA" },
            { "PE",  "es-PE" },
            { "PH",  "en-PH" },
            { "PK",  "ur-PK" },
            { "PL",  "pl-PL" },
            { "PR",  "es-PR" },
            { "PT",  "pt-PT" },
            { "PY",  "es-PY" },
            { "QA",  "ar-QA" },
            { "RO",  "ro-RO" },
            { "RS",  "sr-Latn-RS" },
            { "RU",  "ru-RU" },
            { "RW",  "rw-RW" },
            { "SA",  "ar-SA" },
            { "SE",  "sv-SE" },
            { "SG",  "zh-SG" },
            { "SI",  "sl-SI" },
            { "SK",  "sk-SK" },
            { "SN",  "wo-SN" },
            { "SV",  "es-SV" },
            { "SY",  "ar-SY" },
            { "TH",  "th-TH" },
            { "TJ",  "tg-Cyrl-TJ" },
            { "TM",  "tk-TM" },
            { "TN",  "ar-TN" },
            { "TR",  "tr-TR" },
            { "TT",  "en-TT" },
            { "TW",  "zh-TW" },
            { "UA",  "uk-UA" },
            { "US",  "en-US" },
            { "UY",  "es-UY" },
            { "UZ",  "uz-Cyrl-UZ" },
            { "VE",  "es-VE" },
            { "VN",  "vi-VN" },
            { "YE",  "ar-YE" },
            { "ZA",  "af-ZA" },
            { "ZW",  "en-ZW" }
 */