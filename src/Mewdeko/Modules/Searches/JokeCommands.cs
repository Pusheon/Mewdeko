﻿using System.Linq;
using System.Threading.Tasks;
using Discord.Commands;
using Mewdeko._Extensions;
using Mewdeko.Common;
using Mewdeko.Common.Attributes;
using Mewdeko.Modules.Searches.Services;

namespace Mewdeko.Modules.Searches
{
    public partial class Searches
    {
        [Group]
        public class JokeCommands : MewdekoSubmodule<SearchesService>
        {
            [MewdekoCommand]
            [Usage]
            [Description]
            [Aliases]
            public async Task Yomama()
            {
                await ctx.Channel.SendConfirmAsync(await _service.GetYomamaJoke().ConfigureAwait(false))
                    .ConfigureAwait(false);
            }

            [MewdekoCommand]
            [Usage]
            [Description]
            [Aliases]
            public async Task Randjoke()
            {
                var (setup, punchline) = await _service.GetRandomJoke().ConfigureAwait(false);
                await ctx.Channel.SendConfirmAsync(setup, punchline).ConfigureAwait(false);
            }

            [MewdekoCommand]
            [Usage]
            [Description]
            [Aliases]
            public async Task ChuckNorris()
            {
                await ctx.Channel.SendConfirmAsync(await _service.GetChuckNorrisJoke().ConfigureAwait(false))
                    .ConfigureAwait(false);
            }

            [MewdekoCommand]
            [Usage]
            [Description]
            [Aliases]
            public async Task WowJoke()
            {
                if (!_service.WowJokes.Any())
                {
                    await ReplyErrorLocalizedAsync("jokes_not_loaded").ConfigureAwait(false);
                    return;
                }

                var joke = _service.WowJokes[new MewdekoRandom().Next(0, _service.WowJokes.Count)];
                await ctx.Channel.SendConfirmAsync(joke.Question, joke.Answer).ConfigureAwait(false);
            }

            [MewdekoCommand]
            [Usage]
            [Description]
            [Aliases]
            public async Task MagicItem()
            {
                if (!_service.WowJokes.Any())
                {
                    await ReplyErrorLocalizedAsync("magicitems_not_loaded").ConfigureAwait(false);
                    return;
                }

                var item = _service.MagicItems[new MewdekoRandom().Next(0, _service.MagicItems.Count)];

                await ctx.Channel.SendConfirmAsync("✨" + item.Name, item.Description).ConfigureAwait(false);
            }
        }
    }
}