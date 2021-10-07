﻿using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Mewdeko._Extensions;
using Mewdeko.Common;
using Mewdeko.Common.Attributes;
using Mewdeko.Common.Replacements;
using Mewdeko.Services;

namespace Mewdeko.Modules.Administration
{
    public partial class Administration
    {
        [Group]
        public class ServerGreetCommands : MewdekoSubmodule<GreetSettingsService>
        {
            private readonly IHttpClientFactory _httpFactory;

            public ServerGreetCommands(IHttpClientFactory fact)
            {
                _httpFactory = fact;
            }

            [MewdekoCommand]
            [Usage]
            [Description]
            [Aliases]
            [RequireContext(ContextType.Guild)]
            [UserPerm(GuildPerm.ManageGuild)]
            public async Task GreetDel(int timer = 30)
            {
                if (timer < 0 || timer > 600)
                    return;

                await _service.SetGreetDel(ctx.Guild.Id, timer).ConfigureAwait(false);

                if (timer > 0)
                    await ReplyConfirmLocalizedAsync("greetdel_on", timer).ConfigureAwait(false);
                else
                    await ReplyConfirmLocalizedAsync("greetdel_off").ConfigureAwait(false);
            }

            [MewdekoCommand]
            [Aliases]
            [RequireContext(ContextType.Guild)]
            [UserPerm(GuildPerm.ManageGuild)]
            public Task BoostMsg()
            {
                var boostMessage = _service.GetBoostMessage(ctx.Guild.Id);
                return ReplyConfirmLocalizedAsync("boostmsg_cur", boostMessage?.SanitizeMentions());
            }

            [MewdekoCommand]
            [Aliases]
            [RequireContext(ContextType.Guild)]
            [UserPerm(GuildPerm.ManageGuild)]
            public async Task Boost()
            {
                var enabled = await _service.ToggleBoost(ctx.Guild.Id, ctx.Channel.Id);

                if (enabled)
                    await ReplyConfirmLocalizedAsync("boost_on").ConfigureAwait(false);
                else
                    await ReplyConfirmLocalizedAsync("boost_off").ConfigureAwait(false);
            }

            [MewdekoCommand]
            [Aliases]
            [RequireContext(ContextType.Guild)]
            [UserPerm(GuildPerm.ManageGuild)]
            public async Task BoostDel(int timer = 30)
            {
                if (timer < 0 || timer > 600)
                {
                    await ctx.Channel.SendErrorAsync("The max delete time is 600 seconds!");
                    return;
                }

                await _service.SetBoostDel(ctx.Guild.Id, timer);

                if (timer > 0)
                    await ReplyConfirmLocalizedAsync("boostdel_on", timer);
                else
                    await ReplyConfirmLocalizedAsync("boostdel_off").ConfigureAwait(false);
            }

            [MewdekoCommand]
            [Aliases]
            [RequireContext(ContextType.Guild)]
            [UserPerm(GuildPerm.ManageGuild)]
            public async Task BoostMsg([Remainder] string text)
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    await BoostMsg().ConfigureAwait(false);
                    return;
                }

                var sendBoostEnabled = _service.SetBoostMessage(ctx.Guild.Id, ref text);

                await ReplyConfirmLocalizedAsync("boostmsg_new").ConfigureAwait(false);
                if (!sendBoostEnabled)
                    await ReplyConfirmLocalizedAsync("boostmsg_enable", $"{Prefix}boost");
            }


            [MewdekoCommand]
            [Usage]
            [Description]
            [Aliases]
            [RequireContext(ContextType.Guild)]
            [UserPerm(GuildPerm.ManageGuild)]
            public async Task Greet()
            {
                var enabled = await _service.SetGreet(ctx.Guild.Id, ctx.Channel.Id).ConfigureAwait(false);

                if (enabled)
                    await ReplyConfirmLocalizedAsync("greet_on").ConfigureAwait(false);
                else
                    await ReplyConfirmLocalizedAsync("greet_off").ConfigureAwait(false);
            }

            [MewdekoCommand]
            [Usage]
            [Description]
            [Aliases]
            [RequireContext(ContextType.Guild)]
            [UserPerm(GuildPerm.ManageGuild)]
            public async Task GreetHook(ITextChannel chan = null, string name = null, string image = null,
                string text = null)
            {
                if (text is not null && text.ToLower() == "disable")
                {
                    await _service.SetWebGreetURL(ctx.Guild, "");
                    await ctx.Channel.SendConfirmAsync("Greet webhook disabled.");
                    return;
                }

                if (chan is not null && name is not null && image is not null && text is not null &&
                    text?.ToLower() != "disable") return;
                if (image is not null && text is null)
                {
                    using var http = _httpFactory.CreateClient();
                    var uri = new Uri(image);
                    using (var sr = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead)
                        .ConfigureAwait(false))
                    {
                        var imgData = await sr.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                        using var imgStream = imgData.ToStream();
                        var webhook = await chan.CreateWebhookAsync(name, imgStream);
                        var txt = $"https://discord.com/api/webhooks/{webhook.Id}/{webhook.Token}";
                        await _service.SetWebGreetURL(ctx.Guild, txt);
                        var enabled = await _service.SetGreet(ctx.Guild.Id, ctx.Channel.Id).ConfigureAwait(false);
                        if (enabled)
                            await ctx.Channel.SendConfirmAsync("Set the greet webhook and enabled webhook greets");
                        else
                            await ctx.Channel.SendConfirmAsync(
                                $"Set the greet webhook and enabled webhook greets. Please use {Prefix}greet to enable greet messages.");
                    }
                }

                if (ctx.Message.Attachments.Any() && image is null && text is null)
                {
                    using var http = _httpFactory.CreateClient();
                    var tags = ctx.Message.Attachments.FirstOrDefault();
                    var uri = new Uri(tags.Url);
                    using (var sr = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead)
                        .ConfigureAwait(false))
                    {
                        var imgData = await sr.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                        using var imgStream = imgData.ToStream();
                        var webhook = await chan.CreateWebhookAsync(name, imgStream);
                        var txt = $"https://discord.com/api/webhooks/{webhook.Id}/{webhook.Token}";
                        await _service.SetWebGreetURL(ctx.Guild, txt);
                        var enabled = await _service.SetGreet(ctx.Guild.Id, ctx.Channel.Id).ConfigureAwait(false);
                        if (enabled)
                            await ctx.Channel.SendConfirmAsync("Set the greet webhook and enabled webhook greets");
                        else
                            await ctx.Channel.SendConfirmAsync(
                                $"Set the greet webhook and enabled webhook greets. Please use {Prefix}greet to enable greet messages.");
                    }
                }

                if (!ctx.Message.Attachments.Any() && image is null && text is null)
                {
                    var webhook = await chan.CreateWebhookAsync(name);
                    var txt = $"https://discord.com/api/webhooks/{webhook.Id}/{webhook.Token}";
                    await _service.SetWebGreetURL(ctx.Guild, txt);
                    var enabled = await _service.SetGreet(ctx.Guild.Id, ctx.Channel.Id).ConfigureAwait(false);
                    if (enabled)
                        await ctx.Channel.SendConfirmAsync("Set the greet webhook and enabled webhook greets");
                    else
                        await ctx.Channel.SendConfirmAsync(
                            $"Set the greet webhook and enabled webhook greets. Please use {Prefix}greet to enable greet messages.");
                }
            }

            [MewdekoCommand]
            [Usage]
            [Description]
            [Aliases]
            [RequireContext(ContextType.Guild)]
            [UserPerm(GuildPerm.ManageGuild)]
            public async Task GreetHook(string text)
            {
                await GreetHook(null, null, null, text);
            }

            [MewdekoCommand]
            [Usage]
            [Description]
            [Aliases]
            [RequireContext(ContextType.Guild)]
            [UserPerm(GuildPerm.ManageGuild)]
            public Task GreetMsg()
            {
                var greetMsg = _service.GetGreetMsg(ctx.Guild.Id);
                return ReplyConfirmLocalizedAsync("greetmsg_cur", greetMsg?.SanitizeMentions());
            }

            [MewdekoCommand]
            [Usage]
            [Description]
            [Aliases]
            [RequireContext(ContextType.Guild)]
            [UserPerm(GuildPerm.ManageGuild)]
            public async Task GreetMsg([Remainder] string text)
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    await GreetMsg().ConfigureAwait(false);
                    return;
                }

                var sendGreetEnabled = _service.SetGreetMessage(ctx.Guild.Id, ref text);

                await ReplyConfirmLocalizedAsync("greetmsg_new").ConfigureAwait(false);
                if (!sendGreetEnabled)
                    await ReplyConfirmLocalizedAsync("greetmsg_enable", $"`{Prefix}greet`").ConfigureAwait(false);
            }

            [MewdekoCommand]
            [Usage]
            [Description]
            [Aliases]
            [RequireContext(ContextType.Guild)]
            [UserPerm(GuildPerm.ManageGuild)]
            public async Task GreetDm()
            {
                var enabled = await _service.SetGreetDm(ctx.Guild.Id).ConfigureAwait(false);

                if (enabled)
                    await ReplyConfirmLocalizedAsync("greetdm_on").ConfigureAwait(false);
                else
                    await ReplyConfirmLocalizedAsync("greetdm_off").ConfigureAwait(false);
            }

            [MewdekoCommand]
            [Usage]
            [Description]
            [Aliases]
            [RequireContext(ContextType.Guild)]
            [UserPerm(GuildPerm.ManageGuild)]
            public Task GreetDmMsg()
            {
                var dmGreetMsg = _service.GetDmGreetMsg(ctx.Guild.Id);
                return ReplyConfirmLocalizedAsync("greetdmmsg_cur", dmGreetMsg?.SanitizeMentions());
            }

            [MewdekoCommand]
            [Usage]
            [Description]
            [Aliases]
            [RequireContext(ContextType.Guild)]
            [UserPerm(GuildPerm.ManageGuild)]
            public async Task GreetDmMsg([Remainder] string text = null)
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    await GreetDmMsg().ConfigureAwait(false);
                    return;
                }

                var sendGreetEnabled = _service.SetGreetDmMessage(ctx.Guild.Id, ref text);

                await ReplyConfirmLocalizedAsync("greetdmmsg_new").ConfigureAwait(false);
                if (!sendGreetEnabled)
                    await ReplyConfirmLocalizedAsync("greetdmmsg_enable", $"`{Prefix}greetdm`").ConfigureAwait(false);
            }

            [MewdekoCommand]
            [Usage]
            [Description]
            [Aliases]
            [RequireContext(ContextType.Guild)]
            [UserPerm(GuildPerm.ManageGuild)]
            public async Task Bye()
            {
                var enabled = await _service.SetBye(ctx.Guild.Id, ctx.Channel.Id).ConfigureAwait(false);

                if (enabled)
                    await ReplyConfirmLocalizedAsync("bye_on").ConfigureAwait(false);
                else
                    await ReplyConfirmLocalizedAsync("bye_off").ConfigureAwait(false);
            }

            [MewdekoCommand]
            [Usage]
            [Description]
            [Aliases]
            [RequireContext(ContextType.Guild)]
            [UserPerm(GuildPerm.ManageGuild)]
            public Task ByeMsg()
            {
                var byeMsg = _service.GetByeMessage(ctx.Guild.Id);
                return ReplyConfirmLocalizedAsync("byemsg_cur", byeMsg?.SanitizeMentions());
            }

            [MewdekoCommand]
            [Usage]
            [Description]
            [Aliases]
            [RequireContext(ContextType.Guild)]
            [UserPerm(GuildPerm.ManageGuild)]
            public async Task ByeMsg([Remainder] string text)
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    await ByeMsg().ConfigureAwait(false);
                    return;
                }

                var sendByeEnabled = _service.SetByeMessage(ctx.Guild.Id, ref text);

                await ReplyConfirmLocalizedAsync("byemsg_new").ConfigureAwait(false);
                if (!sendByeEnabled)
                    await ReplyConfirmLocalizedAsync("byemsg_enable", $"`{Prefix}bye`").ConfigureAwait(false);
            }

            [MewdekoCommand]
            [Usage]
            [Description]
            [Aliases]
            [RequireContext(ContextType.Guild)]
            [UserPerm(GuildPerm.ManageGuild)]
            public async Task ByeDel(int timer = 30)
            {
                await _service.SetByeDel(ctx.Guild.Id, timer).ConfigureAwait(false);

                if (timer > 0)
                    await ReplyConfirmLocalizedAsync("byedel_on", timer).ConfigureAwait(false);
                else
                    await ReplyConfirmLocalizedAsync("byedel_off").ConfigureAwait(false);
            }


            [MewdekoCommand]
            [Usage]
            [Description]
            [Aliases]
            [RequireContext(ContextType.Guild)]
            [UserPerm(GuildPerm.ManageGuild)]
            [Ratelimit(5)]
            public async Task ByeTest([Remainder] IGuildUser user = null)
            {
                user = user ?? (IGuildUser)Context.User;

                await _service.ByeTest((ITextChannel)Context.Channel, user);
                var enabled = _service.GetByeEnabled(Context.Guild.Id);
                if (!enabled) await ReplyConfirmLocalizedAsync("byemsg_enable", $"`{Prefix}bye`").ConfigureAwait(false);
            }

            [MewdekoCommand]
            [Usage]
            [Description]
            [Aliases]
            [RequireContext(ContextType.Guild)]
            [UserPerm(GuildPerm.ManageGuild)]
            [Ratelimit(5)]
            public async Task BoostTest()
            {
                var replacer = new ReplacementBuilder()
                    .WithServer(ctx.Client as DiscordSocketClient, ctx.Guild as SocketGuild)
                    .WithUser(ctx.User)
                    .Build();
                if (CREmbed.TryParse(_service.GetBoostMessage(ctx.Guild.Id), out var crEmbed))
                {
                    replacer.Replace(crEmbed);
                    if (crEmbed.PlainText != null && crEmbed.IsEmbedValid)
                        await ctx.Channel.SendMessageAsync(crEmbed.PlainText.SanitizeMentions(true),
                            embed: crEmbed.ToEmbed().Build());
                    if (crEmbed.PlainText is null) await ctx.Channel.SendMessageAsync(embed: crEmbed.ToEmbed().Build());
                    if (crEmbed.PlainText != null && !crEmbed.IsEmbedValid)
                        await ctx.Channel.SendMessageAsync(crEmbed.PlainText.SanitizeMentions(true));
                }
                else
                {
                    await ctx.Channel.SendErrorAsync("Either the boostmsg is invalid or you dont have one set.");
                }
            }

            [MewdekoCommand]
            [Usage]
            [Description]
            [Aliases]
            [RequireContext(ContextType.Guild)]
            [UserPerm(GuildPerm.ManageGuild)]
            [Ratelimit(5)]
            public async Task GreetTest([Remainder] IGuildUser user = null)
            {
                user = user ?? (IGuildUser)Context.User;

                await _service.GreetTest((ITextChannel)Context.Channel, user);
                var enabled = _service.GetGreetEnabled(Context.Guild.Id);
                if (!enabled)
                    await ReplyConfirmLocalizedAsync("greetmsg_enable", $"`{Prefix}greet`").ConfigureAwait(false);
            }

            [MewdekoCommand]
            [Usage]
            [Description]
            [Aliases]
            [RequireContext(ContextType.Guild)]
            [UserPerm(GuildPerm.ManageGuild)]
            [Ratelimit(5)]
            public async Task GreetDmTest([Remainder] IGuildUser user = null)
            {
                user = user ?? (IGuildUser)Context.User;

                var channel = await user.CreateDMChannelAsync();
                var success = await _service.GreetDmTest(channel, user);
                if (success)
                    await Context.OkAsync();
                else
                    await Context.WarningAsync();
                var enabled = _service.GetGreetDmEnabled(Context.Guild.Id);
                if (!enabled)
                    await ReplyConfirmLocalizedAsync("greetdmmsg_enable", $"`{Prefix}greetdm`").ConfigureAwait(false);
            }
        }
    }
}