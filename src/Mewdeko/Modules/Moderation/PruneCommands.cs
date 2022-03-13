using Discord;
using Discord.Commands;
using Mewdeko._Extensions;
using Mewdeko.Common;
using Mewdeko.Common.Attributes;
using Mewdeko.Common.TypeReaders.Models;
using Mewdeko.Modules.Moderation.Services;

namespace Mewdeko.Modules.Moderation;

public partial class Moderation
{
    [Group]
    public class PurgeCommands : MewdekoSubmodule<PurgeService>
    {
        private static readonly TimeSpan _twoWeeks = TimeSpan.FromDays(14);
        private readonly IServiceProvider _services;

        public PurgeCommands(IServiceProvider servs) => _services = servs;


        [MewdekoCommand, Usage, Description, Aliases, UserPerm(GuildPermission.ManageMessages),
         RequireContext(ContextType.Guild)]
        public async Task Purge(string? parameter = null)
        {
            var user = await ctx.Guild.GetCurrentUserAsync().ConfigureAwait(false);

            if (parameter is "-s" or "--safe")
                await Service
                    .PurgeWhere((ITextChannel) ctx.Channel, 100, x => x.Author.Id == user.Id && !x.IsPinned)
                    .ConfigureAwait(false);
            else
                await Service.PurgeWhere((ITextChannel) ctx.Channel, 100, x => x.Author.Id == user.Id)
                    .ConfigureAwait(false);
            ctx.Message.DeleteAfter(3);
        }

        // Purge x
        [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild),
         UserPerm(ChannelPermission.ManageMessages), BotPerm(ChannelPermission.ManageMessages), Priority(1)]
        public async Task Purge(int count, string? parameter = null, string? input = null)
        {
            StoopidTime time = null;
            try
            {
                time = StoopidTime.FromInput(input);
            }
            catch (ArgumentException)
            {
                //ignore
            }

            count++;
            switch (count)
            {
                case < 1:
                    return;
                case > 1000:
                    count = 1000;
                    break;
            }

            switch (parameter)
            {
                case "-s":
                case "--safe":
                    await Service.PurgeWhere((ITextChannel) ctx.Channel, count, x => !x.IsPinned)
                        .ConfigureAwait(false);
                    return;
                case "-nb":
                case "--nobots":
                    await Service.PurgeWhere((ITextChannel) ctx.Channel, count, x => !x.Author.IsBot)
                        .ConfigureAwait(false);
                    return;
                case "-ob":
                case "--onlybots":
                    await Service.PurgeWhere((ITextChannel) ctx.Channel, count, x => x.Author.IsBot)
                        .ConfigureAwait(false);
                    break;
                case "-b":
                case "--before":
                    if (time is null)
                        return;
                    if (time.Time > _twoWeeks)
                        return;
                    await Service.PurgeWhere((ITextChannel) ctx.Channel, count,
                        x => DateTimeOffset.Now.Subtract(x.Timestamp).TotalSeconds <= time.Time.TotalSeconds);
                    break;
                case "-a":
                case "--after":
                    if (time is null)
                        return;
                    if (time.Time > _twoWeeks)
                        return;
                    await Service.PurgeWhere((ITextChannel) ctx.Channel, count,
                        x => DateTimeOffset.Now.Subtract(x.Timestamp).TotalSeconds >= time.Time.TotalSeconds);
                    break;
                case "-he":
                case "--hasembed":
                    await Service.PurgeWhere((ITextChannel) ctx.Channel, count, x => x.Embeds.Any());
                    break;
                case "-ne":
                case "--noembed":
                    await Service.PurgeWhere((ITextChannel) ctx.Channel, count, x => !x.Embeds.Any());
                    break;
                case "-c":
                case "--contains":
                    if (input is null)
                        return;
                    await Service.PurgeWhere((ITextChannel) ctx.Channel, count,
                        x => x.Content.ToLowerInvariant().Contains(input));
                    break;
                default:
                    await Service.PurgeWhere((ITextChannel) ctx.Channel, count, _ => true).ConfigureAwait(false);
                    break;
            }
        }

        //Purge @user [x]
        [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild),
         UserPerm(ChannelPermission.ManageMessages), BotPerm(ChannelPermission.ManageMessages), Priority(0)]
        public Task Purge(IGuildUser user, int count = 100, string? parameter = null) => Purge(user.Id, count, parameter);

        //Purge userid [x]
        [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild),
         UserPerm(ChannelPermission.ManageMessages), BotPerm(ChannelPermission.ManageMessages), Priority(0)]
        public async Task Purge(ulong userId, int count = 100, string? parameter = null)
        {
            if (userId == ctx.User.Id)
                count++;

            switch (count)
            {
                case < 1:
                    return;
                case > 1000:
                    count = 1000;
                    break;
            }

            if (parameter is "-s" or "--safe")
                await Service.PurgeWhere((ITextChannel) ctx.Channel, count,
                        m => m.Author.Id == userId && DateTime.UtcNow - m.CreatedAt < _twoWeeks && !m.IsPinned)
                    .ConfigureAwait(false);
            else
                await Service.PurgeWhere((ITextChannel) ctx.Channel, count,
                    m => m.Author.Id == userId && DateTime.UtcNow - m.CreatedAt < _twoWeeks).ConfigureAwait(false);
        }
    }
}