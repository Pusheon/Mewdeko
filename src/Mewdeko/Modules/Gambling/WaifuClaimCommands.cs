using Discord;
using Discord.Commands;
using Fergun.Interactive;
using Fergun.Interactive.Pagination;
using Mewdeko._Extensions;
using Mewdeko.Common.Attributes;
using Mewdeko.Modules.Gambling.Common;
using Mewdeko.Modules.Gambling.Common.Waifu;
using Mewdeko.Modules.Gambling.Services;

namespace Mewdeko.Modules.Gambling;

public partial class Gambling
{
    [Group]
    public class WaifuClaimCommands : GamblingSubmodule<WaifuService>
    {
        private readonly InteractiveService _interactivity;

        public WaifuClaimCommands(GamblingConfigService gamblingConfService, InteractiveService serv) : base(
            gamblingConfService) =>
            _interactivity = serv;

        [MewdekoCommand, Usage, Description, Aliases]
        public async Task WaifuReset()
        {
            var price = Service.GetResetPrice(ctx.User);
            var embed = new EmbedBuilder()
                .WithTitle(GetText("waifu_reset_confirm"))
                .WithDescription(GetText("waifu_reset_price", Format.Bold(price + CurrencySign)));

            if (!await PromptUserConfirmAsync(embed, ctx.User.Id))
                return;

            if (await Service.TryReset(ctx.User))
            {
                await ReplyConfirmLocalizedAsync("waifu_reset");
                return;
            }

            await ReplyErrorLocalizedAsync("waifu_reset_fail");
        }

        [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild)]
        public async Task WaifuClaim(int amount, [Remainder] IUser target)
        {
            if (amount < Config.Waifu.MinPrice)
            {
                await ReplyErrorLocalizedAsync("waifu_isnt_cheap", Config.Waifu.MinPrice + CurrencySign);
                return;
            }

            if (target.Id == ctx.User.Id)
            {
                await ReplyErrorLocalizedAsync("waifu_not_yourself");
                return;
            }

            var (w, isAffinity, result) = await Service.ClaimWaifuAsync(ctx.User, target, amount);

            switch (result)
            {
                case WaifuClaimResult.InsufficientAmount:
                    await ReplyErrorLocalizedAsync("waifu_not_enough",
                        Math.Ceiling(w.Price * (isAffinity ? 0.88f : 1.1f)));
                    return;
                case WaifuClaimResult.NotEnoughFunds:
                    await ReplyErrorLocalizedAsync("not_enough", CurrencySign);
                    return;
            }

            var msg = GetText("waifu_claimed",
                Format.Bold(target.ToString()),
                amount + CurrencySign);
            if (w.Affinity?.UserId == ctx.User.Id)
                msg += $"\n{GetText("waifu_fulfilled", target, w.Price + CurrencySign)}";
            else
                msg = $" {msg}";
            await ctx.Channel.SendConfirmAsync(ctx.User.Mention + msg);
        }

        [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild), Priority(0)]
        public async Task WaifuTransfer(ulong waifuId, IUser newOwner)
        {
            if (!await Service.WaifuTransfer(ctx.User, waifuId, newOwner)
               )
            {
                await ReplyErrorLocalizedAsync("waifu_transfer_fail");
                return;
            }

            await ReplyConfirmLocalizedAsync("waifu_transfer_success",
                Format.Bold(waifuId.ToString()),
                Format.Bold(ctx.User.ToString()),
                Format.Bold(newOwner.ToString()));
        }

        [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild), Priority(1)]
        public async Task WaifuTransfer(IUser waifu, IUser newOwner)
        {
            if (!await Service.WaifuTransfer(ctx.User, waifu.Id, newOwner)
               )
            {
                await ReplyErrorLocalizedAsync("waifu_transfer_fail");
                return;
            }

            await ReplyConfirmLocalizedAsync("waifu_transfer_success",
                Format.Bold(waifu.ToString()),
                Format.Bold(ctx.User.ToString()),
                Format.Bold(newOwner.ToString()));
        }

        [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild), Priority(0)]
        public Task Divorce([Remainder] IGuildUser target) => Divorce(target.Id);

        [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild), Priority(1)]
        public async Task Divorce([Remainder] ulong targetId)
        {
            if (targetId == ctx.User.Id)
                return;

            var (w, result, amount, remaining) = await Service.DivorceWaifuAsync(ctx.User, targetId);

            switch (result)
            {
                case DivorceResult.SucessWithPenalty:
                    await ReplyConfirmLocalizedAsync("waifu_divorced_like", Format.Bold(w.Waifu.ToString()),
                        amount + CurrencySign);
                    break;
                case DivorceResult.Success:
                    await ReplyConfirmLocalizedAsync("waifu_divorced_notlike", amount + CurrencySign);
                    break;
                case DivorceResult.NotYourWife:
                    await ReplyErrorLocalizedAsync("waifu_not_yours");
                    break;
                default:
                    await ReplyErrorLocalizedAsync("waifu_recent_divorce",
                        Format.Bold(((int) remaining?.TotalHours).ToString()),
                        Format.Bold(remaining?.Minutes.ToString()));
                    break;
            }
        }

        [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild)]
        public async Task Affinity([Remainder] IGuildUser? u = null)
        {
            if (u?.Id == ctx.User.Id)
            {
                await ReplyErrorLocalizedAsync("waifu_egomaniac");
                return;
            }

            var (oldAff, sucess, remaining) = await Service.ChangeAffinityAsync(ctx.User, u);
            if (!sucess)
            {
                if (remaining != null)
                    await ReplyErrorLocalizedAsync("waifu_affinity_cooldown",
                        Format.Bold(((int) remaining?.TotalHours).ToString()),
                        Format.Bold(remaining?.Minutes.ToString()));
                else
                    await ReplyErrorLocalizedAsync("waifu_affinity_already");
                return;
            }

            if (u == null)
                await ReplyConfirmLocalizedAsync("waifu_affinity_reset");
            else if (oldAff == null)
                await ReplyConfirmLocalizedAsync("waifu_affinity_set", Format.Bold(u.ToString()));
            else
                await ReplyConfirmLocalizedAsync("waifu_affinity_changed", Format.Bold(oldAff.ToString()),
                    Format.Bold(u.ToString()));
        }

        [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild)]
        public async Task WaifuLb(int page = 1)
        {
            page--;

            switch (page)
            {
                case < 0:
                    return;
                case > 100:
                    page = 100;
                    break;
            }

            var waifus = Service.GetTopWaifuInfoAtPage(page);

            if (!waifus.Any())
            {
                await ReplyConfirmLocalizedAsync("waifus_none");
                return;
            }

            var embed = new EmbedBuilder()
                .WithTitle(GetText("waifus_top_waifus"))
                .WithOkColor();

            var i = 0;
            foreach (var w in waifus)
            {
                var j = i++;
                embed.AddField(efb =>
                    efb.WithName($"#{((page * 9) + j + 1)} - {w.Price}{CurrencySign}").WithValue(w.ToString())
                        .WithIsInline(false));
            }

            await ctx.Channel.EmbedAsync(embed);
        }

        [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild), Priority(1)]
        public Task WaifuInfo([Remainder] IUser? target = null)
        {
            if (target == null)
                target = ctx.User;

            return InternalWaifuInfo(target.Id, target.ToString());
        }

        [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild), Priority(0)]
        public Task WaifuInfo(ulong targetId) => InternalWaifuInfo(targetId);

        private Task InternalWaifuInfo(ulong targetId, string? name = null)
        {
            var wi = Service.GetFullWaifuInfoAsync(targetId);
            var affInfo = WaifuService.GetAffinityTitle(wi.AffinityCount);

            var waifuItems = Service.GetWaifuItems()
                .ToDictionary(x => x.ItemEmoji, x => x);


            var nobody = GetText("nobody");
            var i = 0;
            var itemsStr = !wi.Items.Any()
                ? "-"
                : string.Join("\n", wi.Items
                    .Where(x => waifuItems.TryGetValue(x.ItemEmoji, out _))
                    .OrderBy(x => waifuItems[x.ItemEmoji].Price)
                    .GroupBy(x => x.ItemEmoji)
                    .Select(x => $"{x.Key} x{x.Count(),-3}")
                    .GroupBy(_ => i++ / 2)
                    .Select(x => string.Join(" ", x)));

            var embed = new EmbedBuilder()
                .WithOkColor()
                .WithTitle(
                    $"{GetText("waifu")} {(wi.FullName ?? name ?? targetId.ToString())} - \"the {WaifuService.GetClaimTitle(wi.ClaimCount)}\"")
                .AddField(efb => efb.WithName(GetText("price")).WithValue(wi.Price.ToString()).WithIsInline(true))
                .AddField(efb =>
                    efb.WithName(GetText("claimed_by")).WithValue(wi.ClaimerName ?? nobody).WithIsInline(true))
                .AddField(efb =>
                    efb.WithName(GetText("likes")).WithValue(wi.AffinityName ?? nobody).WithIsInline(true))
                .AddField(efb =>
                    efb.WithName(GetText("changes_of_heart")).WithValue($"{wi.AffinityCount} - \"the {affInfo}\"")
                        .WithIsInline(true))
                .AddField(efb =>
                    efb.WithName(GetText("divorces")).WithValue(wi.DivorceCount.ToString()).WithIsInline(true))
                .AddField(efb => efb.WithName(GetText("gifts")).WithValue(itemsStr).WithIsInline(false))
                .AddField(efb =>
                    efb.WithName($"Waifus ({wi.ClaimCount})")
                        .WithValue(wi.ClaimCount == 0 ? nobody : string.Join("\n", wi.Claims30))
                        .WithIsInline(false));

            return ctx.Channel.EmbedAsync(embed);
        }

        [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild), Priority(1)]
        public async Task WaifuGift(int page = 1)
        {
            if (--page < 0 || page > 3)
                return;

            var waifuItems = Service.GetWaifuItems();
            var paginator = new LazyPaginatorBuilder()
                .AddUser(ctx.User)
                .WithPageFactory(PageFactory)
                .WithFooter(PaginatorFooter.PageNumber | PaginatorFooter.Users)
                .WithMaxPageIndex(waifuItems.Count / 9)
                .WithDefaultEmotes()
                .Build();

            await _interactivity.SendPaginatorAsync(paginator, Context.Channel, TimeSpan.FromMinutes(60));

            async Task<PageBuilder> PageFactory(int page)
            {
                await Task.CompletedTask;
                var embed = new PageBuilder()
                            .WithTitle(GetText("waifu_gift_shop"))
                            .WithOkColor();

                    waifuItems
                        .OrderBy(x => x.Price)
                        .Skip(9 * page)
                        .Take(9)
                        .ForEach(x => embed.AddField($"{x.ItemEmoji} {x.Name}", x.Price, true));

                    return embed;
            }
        }

        [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild), Priority(0)]
        public async Task WaifuGift(string itemName, [Remainder] IUser waifu)
        {
            if (waifu.Id == ctx.User.Id)
                return;

            var allItems = Service.GetWaifuItems();
            var item = allItems.FirstOrDefault(x => x.Name.ToLowerInvariant() == itemName.ToLowerInvariant());
            if (item is null)
            {
                await ReplyErrorLocalizedAsync("waifu_gift_not_exist");
                return;
            }

            var sucess = await Service.GiftWaifuAsync(ctx.User, waifu, item);

            if (sucess)
                await ReplyConfirmLocalizedAsync("waifu_gift",
                    Format.Bold($"{item} {item.ItemEmoji}"),
                    Format.Bold(waifu.ToString()));
            else
                await ReplyErrorLocalizedAsync("not_enough", CurrencySign);
        }
    }
}