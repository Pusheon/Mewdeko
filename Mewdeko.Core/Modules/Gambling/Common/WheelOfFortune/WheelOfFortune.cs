﻿using System.Collections.Immutable;
using System.Threading.Tasks;
using Mewdeko.Common;
using Mewdeko.Core.Services;

namespace Mewdeko.Modules.Gambling.Common.WheelOfFortune
{
    public class WheelOfFortuneGame
    {
        public static readonly ImmutableArray<float> Multipliers = new[]
        {
            1.7f,
            1.5f,
            0.2f,
            0.1f,
            0.3f,
            0.5f,
            1.2f,
            2.4f
        }.ToImmutableArray();

        private readonly long _bet;
        private readonly ICurrencyService _cs;

        private readonly MewdekoRandom _rng;
        private readonly ulong _userId;

        public WheelOfFortuneGame(ulong userId, long bet, ICurrencyService cs)
        {
            _rng = new MewdekoRandom();
            _cs = cs;
            _bet = bet;
            _userId = userId;
        }

        public async Task<Result> SpinAsync()
        {
            var result = _rng.Next(0, Multipliers.Length);

            var amount = (long) (_bet * Multipliers[result]);

            if (amount > 0)
                await _cs.AddAsync(_userId, "Wheel Of Fortune - won", amount, true).ConfigureAwait(false);

            return new Result
            {
                Index = result,
                Amount = amount
            };
        }

        public class Result
        {
            public int Index { get; set; }
            public long Amount { get; set; }
        }
    }
}