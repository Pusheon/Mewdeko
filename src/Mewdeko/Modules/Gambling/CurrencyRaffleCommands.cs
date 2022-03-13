using Discord;
using Discord.Commands;
using Mewdeko._Extensions;
using Mewdeko.Common;
using Mewdeko.Common.Attributes;
using Mewdeko.Modules.Gambling.Common;
using Mewdeko.Modules.Gambling.Services;

namespace Mewdeko.Modules.Gambling;

public partial class Gambling
{
    public class CurrencyRaffleCommands : GamblingSubmodule<CurrencyRaffleService>
    {
        public enum Mixed
        {
            Mixed
        }

        public CurrencyRaffleCommands(GamblingConfigService gamblingConfService) : base(gamblingConfService)
        {
        }

        [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild), Priority(0)]
        public Task RaffleCur(Mixed _, ShmartNumber amount) => RaffleCur(amount, true);

        [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild), Priority(1)]
        public async Task RaffleCur(ShmartNumber amount, bool mixed = false)
        {
            if (!await CheckBetMandatory(amount).ConfigureAwait(false))
                return;

            async Task OnEnded(IUser arg, long won) => await ctx.Channel.SendConfirmAsync(GetText("rafflecur_ended", CurrencyName,
                    Format.Bold(arg.ToString()), won + CurrencySign)).ConfigureAwait(false);

            var res = await Service.JoinOrCreateGame(ctx.Channel.Id,
                    ctx.User, amount, mixed, OnEnded)
                .ConfigureAwait(false);

            if (res.Item1 != null)
            {
                await ctx.Channel.SendConfirmAsync(GetText("rafflecur", res.Item1.GameType.ToString()),
                    string.Join("\n", res.Item1.Users.Select(x => $"{x.DiscordUser} ({x.Amount})")),
                    footer: GetText("rafflecur_joined", ctx.User.ToString())).ConfigureAwait(false);
            }
            else
            {
                switch (res.Item2)
                {
                    case CurrencyRaffleService.JoinErrorType.AlreadyJoinedOrInvalidAmount:
                        await ReplyErrorLocalizedAsync("rafflecur_already_joined").ConfigureAwait(false);
                        break;
                    case CurrencyRaffleService.JoinErrorType.NotEnoughCurrency:
                        await ReplyErrorLocalizedAsync("not_enough", CurrencySign).ConfigureAwait(false);
                        break;
                }
            }
        }
    }
}