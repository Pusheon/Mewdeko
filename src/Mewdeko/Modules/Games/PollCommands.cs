﻿using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Mewdeko._Extensions;
using Mewdeko.Common;
using Mewdeko.Common.Attributes;
using Mewdeko.Services.Database.Models;
using Mewdeko.Modules.Games.Services;

namespace Mewdeko.Modules.Games
{
    public partial class Games
    {
        [Group]
        public class PollCommands : MewdekoSubmodule<PollService>
        {
            private readonly DiscordSocketClient _client;

            public PollCommands(DiscordSocketClient client)
            {
                _client = client;
            }

            [MewdekoCommand]
            [Usage]
            [Description]
            [Aliases]
            [UserPerm(GuildPerm.ManageMessages)]
            [RequireContext(ContextType.Guild)]
            public async Task Poll([Remainder] string arg)
            {
                if (string.IsNullOrWhiteSpace(arg))
                    return;

                var poll = _service.CreatePoll(ctx.Guild.Id,
                    ctx.Channel.Id, arg);
                if (poll == null)
                {
                    await ReplyErrorLocalizedAsync("poll_invalid_input").ConfigureAwait(false);
                    return;
                }

                if (_service.StartPoll(poll))
                    await ctx.Channel
                        .EmbedAsync(new EmbedBuilder()
                            .WithOkColor()
                            .WithTitle(GetText("poll_created", ctx.User.ToString()))
                            .WithDescription(
                                Format.Bold(poll.Question) + "\n\n" +
                                string.Join("\n", poll.Answers
                                    .Select(x => $"`{x.Index + 1}.` {Format.Bold(x.Text)}"))))
                        .ConfigureAwait(false);
                else
                    await ReplyErrorLocalizedAsync("poll_already_running").ConfigureAwait(false);
            }

            [MewdekoCommand]
            [Usage]
            [Description]
            [Aliases]
            [UserPerm(GuildPerm.ManageMessages)]
            [RequireContext(ContextType.Guild)]
            public async Task PollStats()
            {
                if (!_service.ActivePolls.TryGetValue(ctx.Guild.Id, out var pr))
                    return;

                await ctx.Channel.EmbedAsync(GetStats(pr.Poll, GetText("current_poll_results"))).ConfigureAwait(false);
            }

            [MewdekoCommand]
            [Usage]
            [Description]
            [Aliases]
            [UserPerm(GuildPerm.ManageMessages)]
            [RequireContext(ContextType.Guild)]
            public async Task Pollend()
            {
                var channel = (ITextChannel)ctx.Channel;

                Poll p;
                if ((p = _service.StopPoll(ctx.Guild.Id)) == null)
                    return;

                var embed = GetStats(p, GetText("poll_closed"));
                await ctx.Channel.EmbedAsync(embed)
                    .ConfigureAwait(false);
            }

            public EmbedBuilder GetStats(Poll poll, string title)
            {
                var results = poll.Votes.GroupBy(kvp => kvp.VoteIndex)
                    .ToDictionary(x => x.Key, x => x.Sum(kvp => 1));

                var totalVotesCast = results.Sum(x => x.Value);

                var eb = new EmbedBuilder().WithTitle(title);

                var sb = new StringBuilder()
                    .AppendLine(Format.Bold(poll.Question))
                    .AppendLine();

                var stats = poll.Answers
                    .Select(x =>
                    {
                        results.TryGetValue(x.Index, out var votes);

                        return (x.Index, votes, x.Text);
                    })
                    .OrderByDescending(x => x.votes)
                    .ToArray();

                for (var i = 0; i < stats.Length; i++)
                {
                    var (Index, votes, Text) = stats[i];
                    sb.AppendLine(GetText("poll_result",
                        Index + 1,
                        Format.Bold(Text),
                        Format.Bold(votes.ToString())));
                }

                return eb.WithDescription(sb.ToString())
                    .WithFooter(efb => efb.WithText(GetText("x_votes_cast", totalVotesCast)))
                    .WithOkColor();
            }
        }
    }
}