using Discord.Interactions;
using Mewdeko.Modules.Suggestions.Services;
using System.Threading.Tasks;

namespace Mewdeko.Common.Autocompleters;

public class SuggestionAutocompleter : AutocompleteHandler
{
    public SuggestionAutocompleter(SuggestionsService suggest)
        => _suggest = suggest;

    public DiscordSocketClient Client { get; set; }
    private readonly SuggestionsService _suggest;

    public override Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context, IAutocompleteInteraction interaction, IParameterInfo parameter, IServiceProvider services)
    {
        var content = (string)interaction.Data.Current.Value;

        return Task.FromResult(AutocompletionResult.FromSuccess(_suggest.Suggestions(context.Guild?.Id ?? 0)
                                                                                                   .Where(x => $"{x.SuggestionId}{x.Suggestion}".Contains(content))
                                                                                                   .OrderByDescending(x => x.Suggestion.StartsWith(content))
                                                                                                   .ThenByDescending(x => x.SuggestionId.ToString().StartsWith(content))
                                                                                                   .Select(x =>
                                                                                                       new AutocompleteResult($"{x.SuggestionId} | {x.Suggestion}".TrimTo(100), x.SuggestionId))));
    }
}