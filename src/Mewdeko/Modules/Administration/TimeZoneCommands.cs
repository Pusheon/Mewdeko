﻿using System;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Mewdeko._Extensions;
using Mewdeko.Common;
using Mewdeko.Common.Attributes;
using Mewdeko.Common.Extensions.Interactive;
using Mewdeko.Common.Extensions.Interactive.Entities.Page;
using Mewdeko.Common.Extensions.Interactive.Pagination;
using Mewdeko.Common.Extensions.Interactive.Pagination.Lazy;
using Mewdeko.Modules.Administration.Services;

namespace Mewdeko.Modules.Administration
{
    public partial class Administration
    {
        [Group]
        public class TimeZoneCommands : MewdekoSubmodule<GuildTimezoneService>
        {
            private readonly InteractiveService Interactivity;

            public TimeZoneCommands(InteractiveService serv)
            {
                Interactivity = serv;
            }

            [MewdekoCommand]
            [Usage]
            [Description]
            [Aliases]
            [RequireContext(ContextType.Guild)]
            public async Task Timezones(int page = 1)
            {
                page--;

                if (page < 0 || page > 20)
                    return;

                var timezones = TimeZoneInfo.GetSystemTimeZones()
                    .OrderBy(x => x.BaseUtcOffset)
                    .ToArray();
                var timezonesPerPage = 20;

                var curTime = DateTimeOffset.UtcNow;

                var i = 0;
                var timezoneStrings = timezones
                    .Select(x => (x, ++i % 2 == 0))
                    .Select(data =>
                    {
                        var (tzInfo, flip) = data;
                        var nameStr = $"{tzInfo.Id,-30}";
                        var offset = curTime.ToOffset(tzInfo.GetUtcOffset(curTime)).ToString("zzz");
                        if (flip)
                            return $"{offset} {Format.Code(nameStr)}";
                        return $"{Format.Code(offset)} {nameStr}";
                    });

                var paginator = new LazyPaginatorBuilder()
                    .AddUser(ctx.User)
                    .WithPageFactory(PageFactory)
                    .WithFooter(PaginatorFooter.PageNumber | PaginatorFooter.Users)
                    .WithMaxPageIndex(timezones.Length - 1)
                    .WithDefaultEmotes()
                    .Build();

                await Interactivity.SendPaginatorAsync(paginator, Context.Channel, TimeSpan.FromMinutes(60));

                Task<PageBuilder> PageFactory(int page)
                {
                    return Task.FromResult(new PageBuilder()
                        .WithColor(Mewdeko.Services.Mewdeko.OkColor)
                        .WithTitle(GetText("timezones_available"))
                        .WithDescription(string.Join("\n", timezoneStrings
                            .Skip(page * timezonesPerPage)
                            .Take(timezonesPerPage))));
                }
            }

            [MewdekoCommand]
            [Usage]
            [Description]
            [Aliases]
            [RequireContext(ContextType.Guild)]
            public async Task Timezone()
            {
                await ReplyConfirmLocalizedAsync("timezone_guild", _service.GetTimeZoneOrUtc(ctx.Guild.Id))
                    .ConfigureAwait(false);
            }

            [MewdekoCommand]
            [Usage]
            [Description]
            [Aliases]
            [RequireContext(ContextType.Guild)]
            [UserPerm(GuildPerm.Administrator)]
            public async Task Timezone([Remainder] string id)
            {
                TimeZoneInfo tz;
                try
                {
                    tz = TimeZoneInfo.FindSystemTimeZoneById(id);
                }
                catch
                {
                    tz = null;
                }


                if (tz == null)
                {
                    await ReplyErrorLocalizedAsync("timezone_not_found").ConfigureAwait(false);
                    return;
                }

                _service.SetTimeZone(ctx.Guild.Id, tz);

                await ctx.Channel.SendConfirmAsync(tz.ToString()).ConfigureAwait(false);
            }
        }
    }
}