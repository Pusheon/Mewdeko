﻿using Discord;
using Discord.Commands;
using Fergun.Interactive;
using Fergun.Interactive.Pagination;
using Mewdeko._Extensions;
using Mewdeko.Common;
using Mewdeko.Common.Attributes;
using Mewdeko.Modules.Highlights.Services;


namespace Mewdeko.Modules.Highlights;

public class Highlights : MewdekoModuleBase<HighlightsService>
{
    private readonly InteractiveService _interactivity;
    private readonly IServiceProvider _svcs;

    public Highlights(InteractiveService interactivity, IServiceProvider svcs)
    {
        _interactivity = interactivity;
        _svcs = svcs;
    }

    public enum HighlightActions
    {
        Add,
        List,
        Delete,
        Match,
        ToggleIgnore,
        Toggle
    }

    [MewdekoCommand, Aliases, Description, RequireContext(ContextType.Guild)]
    public async Task Highlight(HighlightActions action, [Remainder] string words = null)
    {
        var highlights = Service.GetForGuild(ctx.Guild.Id);
        switch (action)
        {
            case HighlightActions.Add:
                if (string.IsNullOrWhiteSpace(words))
                    return;
                if (highlights.Any() && highlights.Any(x => x.UserId == ctx.User.Id))
                {
                    if (highlights.Select(x => x.Word.ToLower()).Contains(words.ToLower()))
                    {
                        await ctx.Channel.SendErrorAsync("That's already in your highlights!");
                    }
                    else
                    {
                        await Service.AddHighlight(ctx.Guild.Id, ctx.User.Id, words);
                        await ctx.Channel.SendConfirmAsync($"Added {Format.Code(words)} to your highlights!");
                    }
                }
                else
                {
                    await Service.AddHighlight(ctx.Guild.Id, ctx.User.Id, words);
                    await ctx.Channel.SendConfirmAsync($"Added {Format.Code(words)} to your highlights!");
                }
                break;
            case HighlightActions.List:
                var highlightsForUser = highlights.Where(x => x.UserId == ctx.User.Id);
                if (!highlightsForUser.Any())
                {
                    await ctx.Channel.SendErrorAsync("You have no highlights set!");
                    return;
                }
                var paginator = new LazyPaginatorBuilder()
                                .AddUser(ctx.User)
                                .WithPageFactory(PageFactory)
                                .WithFooter(PaginatorFooter.PageNumber | PaginatorFooter.Users)
                                .WithMaxPageIndex(highlightsForUser.Count() / 10)
                                .WithDefaultEmotes()
                                .Build();

                await _interactivity.SendPaginatorAsync(paginator, Context.Channel,
                    TimeSpan.FromMinutes(60));

                async Task<PageBuilder> PageFactory(int page)
                {
                    await Task.CompletedTask;
                    var highlightsEnumerable = highlightsForUser.Skip(page * 10).Take(10).Select(x => x.Word);
                    return new PageBuilder().WithOkColor()
                                     .WithTitle($"{highlightsForUser.Count()} Highlights")
                                     .WithDescription(String.Join("\n", highlightsEnumerable));
                }

                break;
            case HighlightActions.Delete:
                if (string.IsNullOrWhiteSpace(words))
                    return;
                highlightsForUser = highlights.Where(x => x.UserId == ctx.User.Id);
                if (!highlightsForUser.Any())
                {
                    await ctx.Channel.SendErrorAsync("Cannot delete because you have no highlights set!");
                    return;
                }

                if (!highlightsForUser.Select(x => x.Word).Contains(words))
                {
                    await ctx.Channel.SendErrorAsync("This is not in your highlights!");
                    return;
                }

                await Service.RemoveHighlight(ctx.Guild.Id, ctx.User.Id, words);
                await ctx.Channel.SendConfirmAsync($"Successfully removed {Format.Code(words)} from your highlights.");
                break;
            case HighlightActions.Match:
                if (string.IsNullOrWhiteSpace(words))
                    return;
                highlightsForUser = highlights.Where(x => x.UserId == ctx.User.Id);
                if (!highlightsForUser.Any())
                {
                    await ctx.Channel.SendErrorAsync("There are no highlights to match to.");
                    return;
                }

                var matched = highlightsForUser.Where(x => words.ToLower().Contains(x.Word.ToLower()));
                if (!matched.Any())
                {
                    await ctx.Channel.SendErrorAsync("No matches found.");
                    return;
                }
                paginator = new LazyPaginatorBuilder()
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
                    var highlightsEnumerable = matched.Skip(page * 10).Take(10).Select(x => x.Word);
                    return new PageBuilder().WithOkColor()
                                            .WithTitle($"{matched.Count()} Highlights")
                                            .WithDescription(String.Join("\n", highlightsEnumerable));
                }
                
                break;
            
            case HighlightActions.ToggleIgnore:
                if (string.IsNullOrWhiteSpace(words))
                    return;
                var reader1 = new ChannelTypeReader<ITextChannel>();
                ITextChannel channel;
                var result = await reader1.ReadAsync(ctx, words, _svcs);
                if (!result.IsSuccess)
                {
                    var reader2 = new UserTypeReader<IUser>();
                    IUser host;
                    var result1 = await reader2.ReadAsync(ctx, words, null);
                    host = (IUser) result1.BestMatch;
                    if (host.Username is null)
                    {
                        await ctx.Channel.SendErrorAsync("That user or channel wasnt found!");
                        return;
                    }
                    if (await Service.ToggleIgnoredUser(ctx.Guild.Id, ctx.User.Id, host.Id.ToString()))
                    {
                        await ctx.Channel.SendConfirmAsync($"Added {host.Mention} to ignored users!");
                        return;
                    }
                    else
                        await ctx.Channel.SendConfirmAsync($"Removed {host.Mention} from ignored users!");

                    return;
                }
                channel = (ITextChannel) result.BestMatch;

                if (await Service.ToggleIgnoredChannel(ctx.Guild.Id, ctx.User.Id, channel.Id.ToString()))
                {
                    await ctx.Channel.SendConfirmAsync($"Added {channel.Mention} to ignored channels!");
                }
                else
                    await ctx.Channel.SendConfirmAsync($"Removed {channel.Mention} from ignored channels!");
                break;
            
            case HighlightActions.Toggle:
                if (string.IsNullOrWhiteSpace(words))
                    return;
                if (!bool.TryParse(words, out var enabled))
                {
                    await ctx.Channel.SendErrorAsync("That's gonna be true or false. Not anything else.");
                    return;
                }

                if (enabled)
                {
                    await Service.ToggleHighlights(ctx.Guild.Id, ctx.User.Id, enabled);
                    await ctx.Channel.SendConfirmAsync("Highlights enabled!");
                    return;
                }

                await Service.ToggleHighlights(ctx.Guild.Id, ctx.User.Id, enabled);
                await ctx.Channel.SendConfirmAsync("Highlights disabled.");
                break;
            
        }
    }
    
}