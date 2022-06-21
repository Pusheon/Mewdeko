﻿using Discord.Interactions;
using Mewdeko.Modules.Permissions.Common;
using Mewdeko.Modules.Permissions.Services;
using System.Threading.Tasks;

namespace Mewdeko.Common.Autocompleters;

public class PermissionAutoCompleter : AutocompleteHandler
{
    private PermissionService Perms { get; }
    public PermissionAutoCompleter(PermissionService perms) => Perms = perms;

    public override Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context, IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services)
    {
        var cache = Perms.GetCacheFor(context.Guild.Id);
        var perms = cache.Permissions.Source;
        return Task.FromResult(AutocompletionResult.FromSuccess(perms.Select(x => $"{x.Index}: {x.GetCommand("/", (SocketGuild)context.Guild)}").Take(20)
                                                                     .Where(x => x.Contains((string)autocompleteInteraction.Data.Current.Value)).Select(x =>
                                                                            new AutocompleteResult(x.Length >= 100 ? x[..97] + "..." : x, x.Split(':')[0].Trim()))));
    }
}