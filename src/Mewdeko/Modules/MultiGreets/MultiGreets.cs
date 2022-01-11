﻿using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Humanizer;
using Mewdeko._Extensions;
using Mewdeko.Common;
using Mewdeko.Common.Attributes;
using Mewdeko.Common.Extensions.Interactive;
using Mewdeko.Common.Extensions.Interactive.Entities.Page;
using Mewdeko.Common.Extensions.Interactive.Pagination;
using Mewdeko.Common.Extensions.Interactive.Pagination.Lazy;
using Mewdeko.Common.Replacements;
using Mewdeko.Common.TypeReaders.Models;
using Mewdeko.Modules.MultiGreets.Services;
using System.Net.Http;

namespace Mewdeko.Modules.MultiGreets;

public class MultiGreets : MewdekoModuleBase<MultiGreetService>
{
    private InteractiveService Interactivity;

    public MultiGreets(InteractiveService interactivity) => Interactivity = interactivity;

    public enum MultiGreetTypes
    {
        MultiGreet,
        RandomGreet
    }

    [MewdekoCommand]
    [Aliases]
    [RequireContext(ContextType.Guild)]
    [RequireUserPermission(GuildPermission.Administrator)]
    public async Task MultiGreetAdd ([Remainder] ITextChannel channel = null)
    {
        channel ??= ctx.Channel as ITextChannel;
        var added = Service.AddMultiGreet(ctx.Guild.Id, channel.Id);
        switch (added)
        {
            case true:
                await ctx.Channel.SendConfirmAsync($"Added {channel.Mention} as a MultiGreet channel!");
                break;
            case false:
                await ctx.Channel.SendErrorAsync(
                    "Seems like you have reached your 5 greets per channel limit or your 30 greets per guild limit! Remove a MultiGreet and try again");
                break;
        }
    }

    [MewdekoCommand]
    [Aliases]
    [RequireContext(ContextType.Guild)]
    [RequireUserPermission(GuildPermission.Administrator)]
    public async Task MultiGreetRemove (int id)
    {
        var greet = Service.GetGreets(ctx.Guild.Id).ElementAt(id-1);
        if (greet is null)
        {
            await ctx.Channel.SendErrorAsync("No greet with that ID found!");
            return;
        }

        await Service.RemoveMultiGreetInternal(greet);
        await ctx.Channel.SendConfirmAsync("MultiGreet removed!");
    }
    [MewdekoCommand]
    [Aliases]
    [RequireContext(ContextType.Guild)]
    [RequireUserPermission(GuildPermission.Administrator)]
    public async Task MultiGreetRemove ([Remainder]ITextChannel channel)
    {
        var greet = Service.GetGreets(ctx.Guild.Id).Where(x => x.ChannelId == channel.Id);
        if (!greet.Any())
        {
            await ctx.Channel.SendErrorAsync("There are no greets in that channel!");
            return;
        }

        if (await PromptUserConfirmAsync(new EmbedBuilder().WithOkColor().WithDescription("Are you sure you want to remove all MultiGreets for this channel?"), ctx.User.Id))
        {
            await Service.MultiRemoveMultiGreetInternal(greet.ToArray());
            await ctx.Channel.SendConfirmAsync("MultiGreets removed!");
        }
    }

    [MewdekoCommand]
    [Aliases]
    [RequireContext(ContextType.Guild)]
    [RequireUserPermission(GuildPermission.Administrator)]
    [RequireBotPermission(GuildPermission.ManageMessages)]
    public async Task MultiGreetDelete (int id, StoopidTime time)
    {
        var greet = Service.GetGreets(ctx.Guild.Id).ElementAt(id-1);
        if (greet is null)
        {
            await ctx.Channel.SendErrorAsync("No MultiGreet found for that Id!");
            return;
        }

        await Service.ChangeMGDelete(greet, ulong.Parse(time.Time.TotalSeconds.ToString()));
        await ctx.Channel.SendConfirmAsync(
            $"Successfully updated MultiGreet #{id} to delete after {time.Time.Humanize()}.");

    }
    [MewdekoCommand]
    [Aliases]
    [RequireContext(ContextType.Guild)]
    [RequireUserPermission(GuildPermission.Administrator)]
    [RequireBotPermission(GuildPermission.ManageMessages)]
    public async Task MultiGreetDelete (int id, ulong howlong)
    {
        var greet = Service.GetGreets(ctx.Guild.Id).ElementAt(id-1);
        if (greet is null)
        {
            await ctx.Channel.SendErrorAsync("No MultiGreet found for that Id!");
            return;
        }
        
        await Service.ChangeMGDelete(greet, howlong);
        if (howlong > 0)
            await ctx.Channel.SendConfirmAsync(
                $"Successfully updated MultiGreet #{id} to delete after {TimeSpan.FromSeconds(howlong).Humanize()}.");
        else
            await ctx.Channel.SendConfirmAsync($"MultiGreet #{id} will no longer delete.");

    }

    [MewdekoCommand]
    [Aliases]
    [RequireContext(ContextType.Guild)]
    [RequireUserPermission(GuildPermission.Administrator)]
    public async Task MultiGreetType(MultiGreetTypes types)
    {
        switch (types)
        {
            case MultiGreetTypes.MultiGreet:
                await Service.SetMultiGreetType(ctx.Guild, 0);
                await ctx.Channel.SendConfirmAsync("Regular MultiGreet enabled!");
                break;
            case MultiGreetTypes.RandomGreet:
                await Service.SetMultiGreetType(ctx.Guild, 1);
                await ctx.Channel.SendConfirmAsync("RandomGreet enabled!");
                break;
        }
    }
    [MewdekoCommand]
    [Aliases]
    [RequireContext(ContextType.Guild)]
    [RequireUserPermission(GuildPermission.Administrator)]
    [RequireBotPermission(GuildPermission.ManageWebhooks)]
    public async Task MultiGreetWebhook(int id, string name = null, string avatar = null)
    {
        var greet = Service.GetGreets(ctx.Guild.Id).ElementAt(id-1);
        if (greet is null)
        {
            await ctx.Channel.SendErrorAsync("No MultiGreet found for that Id!");
            return;
        }

        if (name is null)
        {
            await Service.ChangeMGWebhook(greet, null);
            await ctx.Channel.SendConfirmAsync($"Webhook disabled for MultiGreet #{id}!");
            return;
        }
        var channel = await ctx.Guild.GetTextChannelAsync(greet.ChannelId);
        if (avatar is not null)
        {
            if (!Uri.IsWellFormedUriString(avatar, UriKind.Absolute))
            {
                await ctx.Channel.SendErrorAsync(
                    "The avatar url used is not a direct url or is invalid! Please use a different url.");
                return;
            }
            var http = new HttpClient();
            using var sr = await http.GetAsync(avatar, HttpCompletionOption.ResponseHeadersRead)
                                     .ConfigureAwait(false);
            var imgData = await sr.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            await using var imgStream = imgData.ToStream();
            var webhook = await channel.CreateWebhookAsync(name, imgStream);
            await Service.ChangeMGWebhook(greet, $"https://discord.com/api/webhooks/{webhook.Id}/{webhook.Token}");
            await ctx.Channel.SendConfirmAsync("Webhook set!");
        }
        else
        {
            var webhook = await channel.CreateWebhookAsync(name);
            await Service.ChangeMGWebhook(greet, $"https://discord.com/api/webhooks/{webhook.Id}/{webhook.Token}");
            await ctx.Channel.SendConfirmAsync("Webhook set!");
        }
    }
    [MewdekoCommand]
    [Aliases]
    [RequireContext(ContextType.Guild)]
    [RequireUserPermission(GuildPermission.Administrator)]
    public async Task MultiGreetMessage(int id, [Remainder]string message = null)
    {
        var greet = Service.GetGreets(ctx.Guild.Id).ElementAt(id-1);
        if (greet is null)
        {
            await ctx.Channel.SendErrorAsync("No MultiGreet found for that Id!");
            return;
        }
        if (message is null)
        {
            var components = new ComponentBuilder().WithButton("Preview", "preview").WithButton("Regular", "regular");
            var msg = await ctx.Channel.SendConfirmAsync(
                "Would you like to view this as regular text or would you like to preview how it actually looks?", components);
            var response = await GetButtonInputAsync(ctx.Channel.Id, msg.Id, ctx.User.Id);
            switch (response)
            {
                case "preview":
                    await msg.DeleteAsync();
                    var replacer = new ReplacementBuilder().WithUser(ctx.User).WithClient(ctx.Client as DiscordSocketClient).WithServer(ctx.Client as DiscordSocketClient, ctx.Guild as SocketGuild).Build();
                    var content = replacer.Replace(greet.Message);
                    if (CREmbed.TryParse(content, out var embedData))
                    {
                        if (embedData.IsEmbedValid && embedData.PlainText is not null)
                        {
                            await ctx.Channel.SendMessageAsync(embedData.PlainText, embed: embedData.ToEmbed().Build());

                        }

                        if (!embedData.IsEmbedValid && embedData.PlainText is not null)
                        {
                            await ctx.Channel.SendMessageAsync(embedData.PlainText);
                        }

                        if (embedData.IsEmbedValid && embedData.PlainText is null)
                        {
                            await ctx.Channel.SendMessageAsync(embed: embedData.ToEmbed().Build());
                        }
                    }
                    else
                    {
                        await ctx.Channel.SendMessageAsync(content);
                    }

                    break;
                case "regular":
                    await msg.DeleteAsync();
                    await ctx.Channel.SendConfirmAsync(greet.Message);
                    break;
            }
        }
        await Service.ChangeMGMessage(greet, message);
        await ctx.Channel.SendConfirmAsync($"MultiGreet Message for MultiGreet #{id} set!");
    }
    
    [MewdekoCommand]
    [Aliases]
    [RequireContext(ContextType.Guild)]
    [RequireUserPermission(GuildPermission.Administrator)]
    public async Task MultiGreetList()
    {
        var greets = Service.GetGreets(ctx.Guild.Id);
        if (!greets.Any())
        {
            await ctx.Channel.SendErrorAsync("No MultiGreets setup!");
        }
        var paginator = new LazyPaginatorBuilder()
                        .AddUser(ctx.User)
                        .WithPageFactory(PageFactory)
                        .WithFooter(PaginatorFooter.PageNumber | PaginatorFooter.Users)
                        .WithMaxPageIndex(greets.Length-1)
                        .WithDefaultEmotes()
                        .Build();

        await Interactivity.SendPaginatorAsync(paginator, Context.Channel,
            TimeSpan.FromMinutes(60));

        Task<PageBuilder> PageFactory(int page)
        {
            var curgreet = greets.Skip(page).FirstOrDefault();
            return Task.FromResult(new PageBuilder().WithDescription($"#{Array.IndexOf(greets, curgreet) + 1}"
                                                            + $"\n`Channel:` {ctx.Guild.GetTextChannelAsync(curgreet.ChannelId).Result.Mention} {curgreet.ChannelId}"
                                                            + $"\n`Delete After:` {curgreet.DeleteTime}s"
                                                            + $"\n`Webhook:` {curgreet.WebhookUrl != null}"
                                                            + $"\n`Message:` {curgreet.Message.TrimTo(1000)}")
                                                    .WithOkColor());
        }
        
    }
}