using System.IO;
using System.Net.Http;
using Discord;
using Discord.Commands;
using Mewdeko._Extensions;
using Mewdeko.Common;
using Mewdeko.Common.Attributes;
using Mewdeko.Modules.Games.Common;
using Mewdeko.Modules.Games.Services;

namespace Mewdeko.Modules.Games;

/* more games
- Shiritori
- Simple RPG adventure
*/
public partial class Games : MewdekoModuleBase<GamesService>
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IImageCache _images;
    private readonly Random _rng = new();

    public Games(IDataCache data, IHttpClientFactory factory)
    {
        _images = data.LocalImages;
        _httpFactory = factory;
    }

    [MewdekoCommand, Usage, Description, Aliases]
    public async Task Choose([Remainder] string? list = null)
    {
        if (string.IsNullOrWhiteSpace(list))
            return;
        var listArr = list.Split(';');
        if (listArr.Length < 2)
            return;
        var rng = new MewdekoRandom();
        await ctx.Channel.SendConfirmAsync("🤔", listArr[rng.Next(0, listArr.Length)]).ConfigureAwait(false);
    }

    [MewdekoCommand, Usage, Description, Aliases]
    public async Task EightBall([Remainder] string? question = null)
    {
        if (string.IsNullOrWhiteSpace(question))
            return;

        var res = Service.GetEightballResponse(question);
        await ctx.Channel.EmbedAsync(new EmbedBuilder().WithColor(Mewdeko.OkColor)
            .WithDescription(ctx.User.ToString())
            .AddField(efb => efb.WithName($"❓ {GetText("question")}").WithValue(question).WithIsInline(false))
            .AddField($"🎱 {GetText("8ball")}", res));
    }

    [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild)]
    public async Task RateGirl(IGuildUser usr)
    {
        var gr = Service.GirlRatings.GetOrAdd(usr.Id, GetGirl);
        var originalStream = await gr.Stream;

        if (originalStream == null)
        {
            await ReplyErrorLocalizedAsync("something_went_wrong").ConfigureAwait(false);
            return;
        }

        await using var imgStream = new MemoryStream();
        lock (gr)
        {
            originalStream.Position = 0;
            originalStream.CopyTo(imgStream);
        }

        imgStream.Position = 0;
        await ctx.Channel.SendFileAsync(imgStream,
            "rating.png",
            Format.Bold($"{ctx.User.Mention} Girl Rating For {usr}"),
            embed: new EmbedBuilder()
                .WithOkColor()
                .AddField(efb => efb.WithName("Hot").WithValue(gr.Hot.ToString("F2")).WithIsInline(true))
                .AddField(efb => efb.WithName("Crazy").WithValue(gr.Crazy.ToString("F2")).WithIsInline(true))
                .AddField(efb => efb.WithName("Advice").WithValue(gr.Advice).WithIsInline(false))
                .WithImageUrl("attachment://rating.png")
                .Build()).ConfigureAwait(false);
    }

    private double NextDouble(double x, double y) => (_rng.NextDouble() * (y - x)) + x;

    private GirlRating GetGirl(ulong uid)
    {
        var rng = new MewdekoRandom();

        var roll = rng.Next(1, 1001);

        var ratings = Service.Ratings.GetAwaiter().GetResult();

        double hot;
        double crazy;
        string advice;
        switch (roll)
        {
            case < 500:
                hot = NextDouble(0, 5);
                crazy = NextDouble(4, 10);
                advice = ratings.Nog;
                break;
            case < 750:
                hot = NextDouble(5, 8);
                crazy = NextDouble(4, (.6 * hot) + 4);
                advice = ratings.Fun;
                break;
            case < 900:
                hot = NextDouble(5, 10);
                crazy = NextDouble((.61 * hot) + 4, 10);
                advice = ratings.Dan;
                break;
            case < 951:
                hot = NextDouble(8, 10);
                crazy = NextDouble(7, (.6 * hot) + 4);
                advice = ratings.Dat;
                break;
            case < 990:
                hot = NextDouble(8, 10);
                crazy = NextDouble(5, 7);
                advice = ratings.Wif;
                break;
            case < 999:
                hot = NextDouble(8, 10);
                crazy = NextDouble(2, 3.99d);
                advice = ratings.Tra;
                break;
            default:
                hot = NextDouble(8, 10);
                crazy = NextDouble(4, 5);
                advice = ratings.Uni;
                break;
        }

        return new GirlRating(_images, _httpFactory, crazy, hot, roll, advice);
    }

    [MewdekoCommand, Usage, Description, Aliases]
    public async Task Linux(string guhnoo, string loonix) =>
        await ctx.Channel.SendConfirmAsync(
            $@"I'd just like to interject for moment. What you're refering to as {loonix}, is in fact, {guhnoo}/{loonix}, or as I've recently taken to calling it, {guhnoo} plus {loonix}. {loonix} is not an operating system unto itself, but rather another free component of a fully functioning {guhnoo} system made useful by the {guhnoo} corelibs, shell utilities and vital system components comprising a full OS as defined by POSIX.

Many computer users run a modified version of the {guhnoo} system every day, without realizing it. Through a peculiar turn of events, the version of {guhnoo} which is widely used today is often called {loonix}, and many of its users are not aware that it is basically the {guhnoo} system, developed by the {guhnoo} Project.

There really is a {loonix}, and these people are using it, but it is just a part of the system they use. {loonix} is the kernel: the program in the system that allocates the machine's resources to the other programs that you run. The kernel is an essential part of an operating system, but useless by itself; it can only function in the context of a complete operating system. {loonix} is normally used in combination with the {guhnoo} operating system: the whole system is basically {guhnoo} with {loonix} added, or {guhnoo}/{loonix}. All the so-called {loonix} distributions are really distributions of {guhnoo}/{loonix}."
        ).ConfigureAwait(false);
}