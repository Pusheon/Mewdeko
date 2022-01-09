﻿using System.Collections.Generic;
using System.Net.Http;
using Discord;
using Discord.Commands;
using Mewdeko._Extensions;
using Mewdeko.Common;
using Mewdeko.Common.Attributes;
using Mewdeko.Common.Extensions.Interactive;
using Mewdeko.Common.Extensions.Interactive.Entities.Page;
using Mewdeko.Common.Extensions.Interactive.Pagination;
using Mewdeko.Common.Extensions.Interactive.Pagination.Lazy;
using Mewdeko.Modules.CustomReactions.Services;

namespace Mewdeko.Modules.CustomReactions;

public class CustomReactions : MewdekoModuleBase<CustomReactionsService>
{
    public enum All
    {
        All
    }

    private readonly IHttpClientFactory _clientFactory;
    private readonly IBotCredentials _creds;
    private readonly InteractiveService Interactivity;

    public CustomReactions(IBotCredentials creds, IHttpClientFactory clientFactory, InteractiveService serv)
    {
        Interactivity = serv;
        _creds = creds;
        _clientFactory = clientFactory;
    }

    private bool AdminInGuildOrOwnerInDm() =>
        ctx.Guild == null && _creds.IsOwner(ctx.User)
        || ctx.Guild != null && ((IGuildUser) ctx.User).GuildPermissions.Administrator;

    [MewdekoCommand]
    [Usage]
    [Description]
    [Aliases]
    [RequireContext(ContextType.Guild)]
    [UserPerm(GuildPermission.Administrator)]
    public async Task CrsExport()
    {
        if (!AdminInGuildOrOwnerInDm())
        {
            await ReplyErrorLocalizedAsync("insuff_perms").ConfigureAwait(false);
            return;
        }

        _ = ctx.Channel.TriggerTypingAsync();

        var serialized = Service.ExportCrs(ctx.Guild?.Id);
        await using var stream = await serialized.ToStream();
        await ctx.Channel.SendFileAsync(stream, "crs-export.yml");
    }

    [MewdekoCommand]
    [Usage]
    [Description]
    [Aliases]
    [RequireContext(ContextType.Guild)]
    [UserPerm(GuildPermission.Administrator)]
    public async Task CrsImport([Remainder] string input = null)
    {
        if (!AdminInGuildOrOwnerInDm())
        {
            await ReplyErrorLocalizedAsync("insuff_perms").ConfigureAwait(false);
            return;
        }

        input = input?.Trim();

        _ = ctx.Channel.TriggerTypingAsync();

        if (input is null)
        {
            var attachment = ctx.Message.Attachments.FirstOrDefault();
            if (attachment is null)
            {
                await ReplyErrorLocalizedAsync("expr_import_no_input");
                return;
            }

            using var client = _clientFactory.CreateClient();
            input = await client.GetStringAsync(attachment.Url);

            if (string.IsNullOrWhiteSpace(input))
            {
                await ReplyErrorLocalizedAsync("expr_import_no_input");
                return;
            }
        }

        var succ = await Service.ImportCrsAsync(ctx.Guild?.Id, input);
        if (!succ)
        {
            await ReplyErrorLocalizedAsync("expr_import_invalid_data");
            return;
        }

        await ctx.OkAsync();
    }

    [MewdekoCommand]
    [Usage]
    [Description]
    [Aliases]
    public async Task AddCustReact(string key, [Remainder] string message)
    {
        var channel = ctx.Channel as ITextChannel;
        if (string.IsNullOrWhiteSpace(message) || string.IsNullOrWhiteSpace(key))
            return;

        if (!AdminInGuildOrOwnerInDm())
        {
            await ReplyErrorLocalizedAsync("insuff_perms").ConfigureAwait(false);
            return;
        }

        var cr = await Service.AddAsync(ctx.Guild?.Id, key, message);

        await ctx.Channel.EmbedAsync(new EmbedBuilder().WithOkColor()
            .WithTitle(GetText("new_cust_react"))
            .WithDescription($"#{cr.Id}")
            .AddField(efb => efb.WithName(GetText("trigger")).WithValue(key))
            .AddField(efb =>
                efb.WithName(GetText("response"))
                    .WithValue(message.Length > 1024 ? GetText("redacted_too_long") : message))
        ).ConfigureAwait(false);
    }

    [MewdekoCommand]
    [Usage]
    [Description]
    [Aliases]
    public async Task EditCustReact(int id, [Remainder] string message)
    {
        var channel = ctx.Channel as ITextChannel;
        if (string.IsNullOrWhiteSpace(message) || id < 0)
            return;

        if (channel == null && !_creds.IsOwner(ctx.User) ||
            channel != null && !((IGuildUser) ctx.User).GuildPermissions.Administrator)
        {
            await ReplyErrorLocalizedAsync("insuff_perms").ConfigureAwait(false);
            return;
        }

        var cr = await Service.EditAsync(ctx.Guild?.Id, id, message).ConfigureAwait(false);
        if (cr != null)
            await ctx.Channel.EmbedAsync(new EmbedBuilder().WithOkColor()
                .WithTitle(GetText("edited_cust_react"))
                .WithDescription($"#{id}")
                .AddField(efb => efb.WithName(GetText("trigger")).WithValue(cr.Trigger))
                .AddField(efb =>
                    efb.WithName(GetText("response"))
                        .WithValue(message.Length > 1024 ? GetText("redacted_too_long") : message))
            ).ConfigureAwait(false);
        else
            await ReplyErrorLocalizedAsync("edit_fail").ConfigureAwait(false);
    }

    [MewdekoCommand]
    [Usage]
    [Description]
    [Aliases]
    [Priority(1)]
    public async Task ListCustReact(int page = 1)
    {
        if (--page < 0 || page > 999)
            return;

        var customReactions = Service.GetCustomReactionsFor(ctx.Guild?.Id);

        if (customReactions == null || !customReactions.Any())
        {
            await ReplyErrorLocalizedAsync("no_found").ConfigureAwait(false);
            return;
        }

        var paginator = new LazyPaginatorBuilder()
            .AddUser(ctx.User)
            .WithPageFactory(PageFactory)
            .WithFooter(PaginatorFooter.PageNumber | PaginatorFooter.Users)
            .WithMaxPageIndex(customReactions.Length / 20)
            .WithDefaultEmotes()
            .Build();

        await Interactivity.SendPaginatorAsync(paginator, Context.Channel, TimeSpan.FromMinutes(60));

        Task<PageBuilder> PageFactory(int page)
        {
            return Task.FromResult(new PageBuilder().WithColor(Mewdeko.Services.Mewdeko.OkColor)
                .WithTitle(GetText("custom_reactions"))
                .WithDescription(string.Join("\n", customReactions.OrderBy(cr => cr.Trigger)
                    .Skip(page * 20)
                    .Take(20)
                    .Select(cr =>
                    {
                        var str = $"`#{cr.Id}` {cr.Trigger}";
                        if (cr.AutoDeleteTrigger) str = "🗑" + str;
                        if (cr.DmResponse) str = "📪" + str;
                        var reactions = cr.GetReactions();
                        if (reactions.Any()) str = str + " // " + string.Join(" ", reactions);

                        return str;
                    }))));
        }
    }


    [MewdekoCommand]
    [Usage]
    [Description]
    [Aliases]
    public async Task ListCustReactG(int page = 1)
    {
        if (--page < 0 || page > 9999)
            return;
        var customReactions = Service.GetCustomReactionsFor(ctx.Guild?.Id);

        if (customReactions == null || !customReactions.Any())
        {
            await ReplyErrorLocalizedAsync("no_found").ConfigureAwait(false);
        }
        else
        {
            var ordered = customReactions
                .GroupBy(cr => cr.Trigger)
                .OrderBy(cr => cr.Key)
                .ToList();

            var paginator = new LazyPaginatorBuilder()
                .AddUser(ctx.User)
                .WithPageFactory(PageFactory)
                .WithFooter(PaginatorFooter.PageNumber | PaginatorFooter.Users)
                .WithMaxPageIndex(customReactions.Length / 20)
                .WithDefaultEmotes()
                .Build();

            await Interactivity.SendPaginatorAsync(paginator, Context.Channel, TimeSpan.FromMinutes(60));

            Task<PageBuilder> PageFactory(int page)
            {
                return Task.FromResult(new PageBuilder().WithColor(Mewdeko.Services.Mewdeko.OkColor)
                    .WithTitle(GetText("name"))
                    .WithDescription(string.Join("\r\n", ordered
                        .Skip(page * 20)
                        .Take(20)
                        .Select(cr => $"**{cr.Key.Trim().ToLowerInvariant()}** `x{cr.Count()}`"))));
            }
        }
    }

    [MewdekoCommand]
    [Usage]
    [Description]
    [Aliases]
    public async Task ShowCustReact(int id)
    {
        var found = Service.GetCustomReaction(ctx.Guild?.Id, id);

        if (found == null)
            await ReplyErrorLocalizedAsync("no_found_id").ConfigureAwait(false);
        else
            await ctx.Channel.EmbedAsync(new EmbedBuilder().WithOkColor()
                .WithDescription($"#{id}")
                .AddField(efb => efb.WithName(GetText("trigger")).WithValue(found.Trigger.TrimTo(1024)))
                .AddField(efb =>
                    efb.WithName(GetText("response"))
                        .WithValue((found.Response + "\n```css\n" + found.Response).TrimTo(1020) + "```"))
            ).ConfigureAwait(false);
    }

    [MewdekoCommand]
    [Usage]
    [Description]
    [Aliases]
    public async Task DelCustReact(int id)
    {
        if (!AdminInGuildOrOwnerInDm())
        {
            await ReplyErrorLocalizedAsync("insuff_perms").ConfigureAwait(false);
            return;
        }

        var cr = await Service.DeleteAsync(ctx.Guild?.Id, id);

        if (cr != null)
            await ctx.Channel.EmbedAsync(new EmbedBuilder().WithOkColor()
                    .WithTitle(GetText("deleted"))
                    .WithDescription("#" + cr.Id)
                    .AddField(efb => efb.WithName(GetText("trigger")).WithValue(cr.Trigger.TrimTo(1024)))
                    .AddField(efb => efb.WithName(GetText("response")).WithValue(cr.Response.TrimTo(1024))))
                .ConfigureAwait(false);
        else
            await ReplyErrorLocalizedAsync("no_found_id").ConfigureAwait(false);
    }

    [MewdekoCommand]
    [Usage]
    [Description]
    [Aliases]
    public async Task CrReact(int id, params string[] emojiStrs)
    {
        if (!AdminInGuildOrOwnerInDm())
        {
            await ReplyErrorLocalizedAsync("insuff_perms").ConfigureAwait(false);
            return;
        }

        var cr = Service.GetCustomReaction(Context.Guild?.Id, id);
        if (cr is null)
        {
            await ReplyErrorLocalizedAsync("no_found").ConfigureAwait(false);
            return;
        }

        if (emojiStrs.Length == 0)
        {
            await Service.ResetCrReactions(ctx.Guild?.Id, id);
            await ReplyConfirmLocalizedAsync("crr_reset", Format.Bold(id.ToString())).ConfigureAwait(false);
            return;
        }

        var succ = new List<string>();
        foreach (var emojiStr in emojiStrs)
        {
            var emote = emojiStr.ToIEmote();

            // i should try adding these emojis right away to the message, to make sure the bot can react with these emojis. If it fails, skip that emoji
            try
            {
                await Context.Message.AddReactionAsync(emote).ConfigureAwait(false);
                await Task.Delay(100).ConfigureAwait(false);
                succ.Add(emojiStr);

                if (succ.Count >= 6)
                    break;
            }
            catch
            {
            }
        }

        if (succ.Count == 0)
        {
            await ReplyErrorLocalizedAsync("invalid_emojis").ConfigureAwait(false);
            return;
        }

        await Service.SetCrReactions(ctx.Guild?.Id, id, succ);


        await ReplyConfirmLocalizedAsync("crr_set", Format.Bold(id.ToString()),
            string.Join(", ", succ.Select(x => x.ToString()))).ConfigureAwait(false);
    }

    [MewdekoCommand]
    [Usage]
    [Description]
    [Aliases]
    public Task CrCa(int id) => InternalCrEdit(id, CustomReactionsService.CrField.ContainsAnywhere);

    [MewdekoCommand]
    [Usage]
    [Description]
    [Aliases]
    public Task Rtt(int id) => InternalCrEdit(id, CustomReactionsService.CrField.ReactToTrigger);

    [MewdekoCommand]
    [Usage]
    [Description]
    [Aliases]
    public Task CrDm(int id) => InternalCrEdit(id, CustomReactionsService.CrField.DmResponse);

    [MewdekoCommand]
    [Usage]
    [Description]
    [Aliases]
    public Task CrAd(int id) => InternalCrEdit(id, CustomReactionsService.CrField.AutoDelete);

    [MewdekoCommand]
    [Usage]
    [Description]
    [Aliases]
    public Task CrAt(int id) => InternalCrEdit(id, CustomReactionsService.CrField.AllowTarget);
    [MewdekoCommand]
    [Usage]
    [Description]
    [Aliases]
    public Task CrNr(int id) => InternalCrEdit(id, CustomReactionsService.CrField.NoRespond);


    [MewdekoCommand]
    [Usage]
    [Description]
    [Aliases]
    [OwnerOnly]
    public async Task CrsReload()
    {
        await Service.TriggerReloadCustomReactions();

        await ctx.OkAsync();
    }

    private async Task InternalCrEdit(int id, CustomReactionsService.CrField option)
    {
        if (!AdminInGuildOrOwnerInDm())
        {
            await ReplyErrorLocalizedAsync("insuff_perms").ConfigureAwait(false);
            return;
        }

        var (success, newVal) = await Service.ToggleCrOptionAsync(id, option).ConfigureAwait(false);
        if (!success)
        {
            await ReplyErrorLocalizedAsync("no_found_id").ConfigureAwait(false);
            return;
        }

        if (newVal)
            await ReplyConfirmLocalizedAsync("option_enabled", Format.Code(option.ToString()),
                Format.Code(id.ToString())).ConfigureAwait(false);
        else
            await ReplyConfirmLocalizedAsync("option_disabled", Format.Code(option.ToString()),
                Format.Code(id.ToString())).ConfigureAwait(false);
    }

    [MewdekoCommand]
    [Usage]
    [Description]
    [Aliases]
    [RequireContext(ContextType.Guild)]
    [UserPerm(GuildPermission.Administrator)]
    public async Task CrClear()
    {
        if (await PromptUserConfirmAsync(new EmbedBuilder()
                    .WithTitle("Custom reaction clear")
                    .WithDescription("This will delete all custom reactions on this server."), ctx.User.Id)
                .ConfigureAwait(false))
        {
            var count = Service.DeleteAllCustomReactions(ctx.Guild.Id);
            await ReplyConfirmLocalizedAsync("cleared", count).ConfigureAwait(false);
        }
    }
}