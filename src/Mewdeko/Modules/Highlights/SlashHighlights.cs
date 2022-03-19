using Discord;
using Discord.WebSocket;
using Fergun.Interactive;
using Fergun.Interactive.Pagination;
using Discord.Interactions;
using Mewdeko._Extensions;
using Mewdeko.Common;
using Mewdeko.Common.Attributes;
using Mewdeko.Common.Autocompleters;
using Mewdeko.Database;
using Mewdeko.Database.Extensions;
using Mewdeko.Modules.Highlights.Services;

namespace Mewdeko.Modules.Highlights;

[Group("highlights", "Set or manage highlights")]
public class SlashHighlights : MewdekoSlashModuleBase<HighlightsService>
{
    private readonly InteractiveService _interactivity;
    private readonly DbService _db;

    public SlashHighlights(InteractiveService interactivity, DbService db)
    {
        _interactivity = interactivity;
        _db = db;
    }

    [SlashCommand("add", "Add new highlights."), RequireContext(ContextType.Guild), CheckPermissions, BlacklistCheck]
    public async Task AddHighlight([Summary("words", "Words to highlight.")] string words)
    {
        await using var uow = _db.GetDbContext();
        var highlights = uow.Highlights.ForUser(ctx.Guild.Id, ctx.User.Id).ToList();
        if (string.IsNullOrWhiteSpace(words))
        {
            await ctx.Interaction.SendErrorAsync("You need to specify a phrase to highlight.");
            return;
        }

        if (highlights.Any() && highlights.Select(x => x.Word.ToLower()).Contains(words.ToLower()))
            await ctx.Interaction.SendErrorAsync("That's already in your highlights");
        else
        {
            await Service.AddHighlight(ctx.Guild.Id, ctx.User.Id, words);
            await ctx.Interaction.SendConfirmAsync($"Added {Format.Code(words)} to your highlights!");
        }
    }

    [SlashCommand("list", "List your current highlights."), RequireContext(ContextType.Guild), CheckPermissions, BlacklistCheck]
    public async Task ListHighlights()
    {
        await using var uow = _db.GetDbContext();
        var highlightsForUser = uow.Highlights.ForUser(ctx.Guild.Id, ctx.User.Id).ToList();

        if (!highlightsForUser.Any())
        {
            await ctx.Interaction.SendErrorAsync("You have no highlights set.");
            return;
        }

        var paginator = new LazyPaginatorBuilder()
            .AddUser(ctx.User)
            .WithPageFactory(PageFactory)
            .WithFooter(PaginatorFooter.PageNumber | PaginatorFooter.Users)
            .WithMaxPageIndex(highlightsForUser.Count() / 10)
            .WithDefaultEmotes()
            .Build();

        await _interactivity.SendPaginatorAsync(paginator, ctx.Interaction as SocketInteraction,
            TimeSpan.FromMinutes(60));

        async Task<PageBuilder> PageFactory(int page)
        {
            await Task.CompletedTask;
            var highlightsEnumerable = highlightsForUser.Skip(page * 10).Take(10);
            return new PageBuilder().WithOkColor()
                             .WithTitle($"{highlightsForUser.Count()} Highlights")
                             .WithDescription(string.Join("\n", highlightsEnumerable.Select(x => $"{highlightsForUser.IndexOf(x) + 1}. {x.Word}")));
        }
    }

    [SlashCommand("delete", "Delete a highlight."), RequireContext(ContextType.Guild), CheckPermissions, BlacklistCheck]
    public async Task DeleteHighlight(
        [Autocomplete(typeof(HighlightAutocompleter)), Summary("words", "The highlight to delete.")] string words)
    {

        if (string.IsNullOrWhiteSpace(words))
        {
            await ctx.Interaction.SendErrorAsync("Cannot delete an empty highlight.");
            return;
        }

        await using var uow = _db.GetDbContext();
        var highlightsForUser = uow.Highlights.ForUser(ctx.Guild.Id, ctx.User.Id).ToList();

        if (!highlightsForUser.Any())
        {
            await ctx.Interaction.SendErrorAsync("Cannot delete because you have no highlights set!");
            return;
        }

        if (int.TryParse(words, out var number))
        {
            var todelete = highlightsForUser.ElementAt(number - 1);
            if (todelete is null)
            {
                await ctx.Interaction.SendErrorAsync("That Highlight does not exist!");
                return;
            }

            await Service.RemoveHighlight(todelete);
            await ctx.Interaction.SendConfirmAsync($"Successfully removed {Format.Code(todelete.Word)} from your highlights.");
            return;
        }
        if (!highlightsForUser.Select(x => x.Word).Contains(words))
        {
            await ctx.Interaction.SendErrorAsync("This is not in your highlights!");
            return;
        }
        await Service.RemoveHighlight(highlightsForUser.FirstOrDefault(x => x.Word == words));
        await ctx.Interaction.SendConfirmAsync($"Successfully removed {Format.Code(words)} from your highlights.");
    }

    [SlashCommand("match", "Find a matching highlight."), RequireContext(ContextType.Guild), CheckPermissions, BlacklistCheck]
    public async Task MatchHighlight(
        [Autocomplete(typeof(HighlightAutocompleter)), Summary("words", "The highlight to find.")] string words)
    {
        if (string.IsNullOrWhiteSpace(words))
        {
            await ctx.Interaction.SendErrorAsync("Cannot match an empty highlight.");
            return;
        }

        await using var uow = _db.GetDbContext();
        var highlightsForUser = uow.Highlights.ForUser(ctx.Guild.Id, ctx.User.Id).ToList();

        var matched = highlightsForUser.Where(x => words.ToLower().Contains(x.Word.ToLower()));
        if (!matched.Any())
        {
            await ctx.Interaction.SendErrorAsync("No matches found.");
            return;
        }

        var paginator = new LazyPaginatorBuilder()
            .AddUser(ctx.User)
            .WithPageFactory(PageFactory1)
            .WithFooter(PaginatorFooter.PageNumber | PaginatorFooter.Users)
            .WithMaxPageIndex(matched.Count() / 10)
            .WithDefaultEmotes()
            .Build();

        await _interactivity.SendPaginatorAsync(paginator, Context.Channel,
            TimeSpan.FromMinutes(60));

        async Task<PageBuilder> PageFactory1(int page)
        {
            await Task.CompletedTask;
            var highlightsEnumerable = matched.Skip(page * 10).Take(10);
            return new PageBuilder().WithOkColor()
                            .WithTitle($"{highlightsForUser.Count()} Highlights")
                            .WithDescription(string.Join("\n", highlightsEnumerable.Select(x => $"{highlightsForUser.IndexOf(x) + 1}. {x.Word}")));
        }
    }

    [SlashCommand("toggle-user", "Ignore a specified user."), RequireContext(ContextType.Guild), CheckPermissions, BlacklistCheck]
    public async Task ToggleUser(IUser user)
    {
        if (await Service.ToggleIgnoredUser(ctx.Guild.Id, ctx.User.Id, user.Id.ToString()))
        {
            await ctx.Interaction.SendConfirmAsync($"Added {user.Mention} to ignored users!");
            return;
        }
        await ctx.Interaction.SendConfirmAsync($"Removed {user.Mention} from ignored users!");
    }

    [SlashCommand("toggle-channel", "Ignore a specified channel."), RequireContext(ContextType.Guild), CheckPermissions, BlacklistCheck]
    public async Task ToggleChannel(ITextChannel channel)
    {
        if (await Service.ToggleIgnoredUser(ctx.Guild.Id, ctx.User.Id, channel.Id.ToString()))
        {
            await ctx.Interaction.SendConfirmAsync($"Added {channel.Mention} to ignored channels!");
            return;
        }
        await ctx.Interaction.SendConfirmAsync($"Removed {channel.Mention} from ignored channels!");
    }

    [SlashCommand("toggle-global", "Enable or disable highlights globally."), RequireContext(ContextType.Guild), CheckPermissions, BlacklistCheck]
    public async Task ToggleGlobal([Summary("enabled", "Are highlights enabled globally?")] bool enabled)
    {
        if (enabled)
        {
            await Service.ToggleHighlights(ctx.Guild.Id, ctx.User.Id, enabled);
            await ctx.Interaction.SendConfirmAsync("Highlights enabled!");
            return;
        }

        await Service.ToggleHighlights(ctx.Guild.Id, ctx.User.Id, enabled);
        await ctx.Interaction.SendConfirmAsync("Highlights disabled.");
    }
}
