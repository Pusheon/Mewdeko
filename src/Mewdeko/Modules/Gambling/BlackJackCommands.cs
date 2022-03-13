using Discord;
using Discord.Commands;
using Mewdeko._Extensions;
using Mewdeko.Common;
using Mewdeko.Common.Attributes;
using Mewdeko.Database;
using Mewdeko.Database.Extensions;
using Mewdeko.Modules.Gambling.Common;
using Mewdeko.Modules.Gambling.Common.Blackjack;
using Mewdeko.Modules.Gambling.Services;
using Serilog;

namespace Mewdeko.Modules.Gambling;

public partial class Gambling
{
    public class BlackJackCommands : GamblingSubmodule<BlackJackService>
    {
        public enum BjAction
        {
            Hit = int.MinValue,
            Stand,
            Double
        }

        private readonly ICurrencyService _cs;
        private readonly DbService _db;
        private IUserMessage msg;

        public BlackJackCommands(ICurrencyService cs, DbService db,
            GamblingConfigService gamblingConf) : base(gamblingConf)
        {
            _cs = cs;
            _db = db;
        }

        [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild)]
        public async Task BlackJack(ShmartNumber amount)
        {
            if (!await CheckBetMandatory(amount).ConfigureAwait(false))
                return;

            var newBj = new Blackjack(_cs, _db);
            Blackjack bj;
            if (newBj == (bj = Service.Games.GetOrAdd(ctx.Channel.Id, newBj)))
            {
                if (!await bj.Join(ctx.User, amount).ConfigureAwait(false))
                {
                    Service.Games.TryRemove(ctx.Channel.Id, out _);
                    await ReplyErrorLocalizedAsync("not_enough", CurrencySign).ConfigureAwait(false);
                    return;
                }

                bj.StateUpdated += Bj_StateUpdated;
                bj.GameEnded += Bj_GameEnded;
                bj.Start();

                await ReplyConfirmLocalizedAsync("bj_created").ConfigureAwait(false);
            }
            else
            {
                if (await bj.Join(ctx.User, amount).ConfigureAwait(false))
                    await ReplyConfirmLocalizedAsync("bj_joined").ConfigureAwait(false);
                else
                    Log.Information($"{ctx.User} can't join a blackjack game as it's in " + bj.State +
                                    " state already.");
            }

            await ctx.Message.DeleteAsync().ConfigureAwait(false);
        }

        private Task Bj_GameEnded(Blackjack arg)
        {
            Service.Games.TryRemove(ctx.Channel.Id, out _);
            return Task.CompletedTask;
        }

        private async Task Bj_StateUpdated(Blackjack bj)
        {
            try
            {
                if (msg != null)
                {
                    var _ = msg.DeleteAsync();
                }

                var c = bj.Dealer.Cards.Select(x => x.GetEmojiString());
                var dealerIcon = "❔ ";
                if (bj.State == Blackjack.GameState.Ended)
                {
                    if (bj.Dealer.GetHandValue() == 21)
                        dealerIcon = "💰 ";
                    else if (bj.Dealer.GetHandValue() > 21)
                        dealerIcon = "💥 ";
                    else
                        dealerIcon = "🏁 ";
                }

                var cStr = string.Concat(c.Select(x => $"{x[..^1]} "));
                cStr += $"\n{string.Concat(c.Select(x => $"{x.Last()} "))}";
                var embed = new EmbedBuilder()
                    .WithOkColor()
                    .WithTitle("BlackJack")
                    .AddField($"{dealerIcon} Dealer's Hand | Value: {bj.Dealer.GetHandValue()}", cStr);

                if (bj.CurrentUser != null)
                    embed.WithFooter($"Player to make a choice: {bj.CurrentUser.DiscordUser}");

                foreach (var p in bj.Players)
                {
                    c = p.Cards.Select(x => x.GetEmojiString());
                    cStr = $"-\t{string.Concat(c.Select(x => $"{x[..^1]} "))}";
                    cStr += $"\n-\t{string.Concat(c.Select(x => $"{x.Last()} "))}";
                    var full = $"{p.DiscordUser.ToString().TrimTo(20)} | Bet: {p.Bet} | Value: {p.GetHandValue()}";
                    if (bj.State == Blackjack.GameState.Ended)
                    {
                        if (p.State == User.UserState.Lost)
                            full = $"❌ {full}";
                        else
                            full = $"✅ {full}";
                    }
                    else if (p == bj.CurrentUser)
                    {
                        full = $"▶ {full}";
                    }
                    else switch (p.State)
                    {
                        case User.UserState.Stand:
                            full = $"⏹ {full}";
                            break;
                        case User.UserState.Bust:
                            full = $"💥 {full}";
                            break;
                        case User.UserState.Blackjack:
                            full = $"💰 {full}";
                            break;
                    }

                    embed.AddField(full, cStr);
                }

                msg = await ctx.Channel.EmbedAsync(embed).ConfigureAwait(false);
            }
            catch
            {
            }
        }

        private static string UserToString(User x)
        {
            var playerName = x.State == User.UserState.Bust
                ? Format.Strikethrough(x.DiscordUser.ToString().TrimTo(30))
                : x.DiscordUser.ToString();

            _ = $"{string.Concat(x.Cards.Select(y => $"〖{y.GetEmojiString()}〗"))}";


            return $"{playerName} | Bet: {x.Bet}\n";
        }

        [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild)]
        public Task Hit() => InternalBlackJack(BjAction.Hit);

        [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild)]
        public Task Stand() => InternalBlackJack(BjAction.Stand);

        [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild)]
        public Task Double() => InternalBlackJack(BjAction.Double);

        public async Task InternalBlackJack(BjAction a)
        {
            if (!Service.Games.TryGetValue(ctx.Channel.Id, out var bj))
                return;

            switch (a)
            {
                case BjAction.Hit:
                    await bj.Hit(ctx.User).ConfigureAwait(false);
                    break;
                case BjAction.Stand:
                    await bj.Stand(ctx.User).ConfigureAwait(false);
                    break;
                case BjAction.Double:
                    {
                        if (!await bj.Double(ctx.User).ConfigureAwait(false))
                            await ReplyErrorLocalizedAsync("not_enough", CurrencySign).ConfigureAwait(false);
                        break;
                    }
            }

            await ctx.Message.DeleteAsync().ConfigureAwait(false);
        }
    }
}