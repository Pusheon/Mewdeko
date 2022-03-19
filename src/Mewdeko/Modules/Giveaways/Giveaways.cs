using Discord;
using Discord.Commands;
using Fergun.Interactive;
using Fergun.Interactive.Pagination;
using Mewdeko._Extensions;
using Mewdeko.Common;
using Mewdeko.Common.Attributes;
using Mewdeko.Common.TypeReaders.Models;
using Mewdeko.Database;
using Mewdeko.Database.Extensions;
using Mewdeko.Modules.Giveaways.Services;
using System.Text.RegularExpressions;

namespace Mewdeko.Modules.Giveaways;

public class Giveaways : MewdekoModuleBase<GiveawayService>
{
    private readonly IServiceProvider _servs;
    private readonly DbService _db;
    private readonly InteractiveService _interactivity;

    public Giveaways(DbService db, IServiceProvider servs, InteractiveService interactiveService)
    {
        _interactivity = interactiveService;
        _db = db;
        _servs = servs;
    }

    [MewdekoCommand, Usage, Description, Aliases, UserPerm(GuildPermission.ManageMessages)]
    public async Task GEmote(IEmote emote)
    {
        try
        {
            await ctx.Message.AddReactionAsync(emote);
        }
        catch
        {
            await ctx.Channel.SendErrorAsync(
                "I'm unable to use that emote for giveaways! Most likely because I'm not in a server with it.");
            return;
        }

        await Service.SetGiveawayEmote(ctx.Guild, emote.ToString());
        await ctx.Channel.SendConfirmAsync(
            $"Giveaway emote set to {emote}! Just keep in mind this doesn't update until the next giveaway.");
    }
    [MewdekoCommand, Usage, Description, Aliases, UserPerm(GuildPermission.ManageMessages)]
    public async Task GReroll(ulong messageid)
    {
        await using var uow = _db.GetDbContext();
        var gway = uow.Giveaways
                      .GiveawaysForGuild(ctx.Guild.Id).ToList().FirstOrDefault(x => x.MessageId == messageid);
        if (gway is null)
        {
            await ctx.Channel.SendErrorAsync("No Giveaway with that message ID exists! Please try again!");
            return;
        }

        if (gway.Ended != 1)
        {
            await ctx.Channel.SendErrorAsync("This giveaway hasn't ended yet!");
            return;
        }

        await Service.GiveawayReroll(gway);
        await ctx.Channel.SendConfirmAsync("Giveaway Rerolled!");
    }

    [MewdekoCommand, Usage, Description, Aliases]
    public async Task GStats()
    {
        var eb = new EmbedBuilder().WithOkColor();
        var gways = _db.GetDbContext().Giveaways.GiveawaysForGuild(ctx.Guild.Id);
        if (!gways.Any())
        {
            await ctx.Channel.SendErrorAsync("There have been no giveaways here, so no stats!");
        }
        else
        {
            List<ITextChannel> gchans = new();
            foreach (var i in gways)
            {
                var chan = await ctx.Guild.GetTextChannelAsync(i.ChannelId);
                if (!gchans.Contains(chan))
                    gchans.Add(chan);
            }

            var amount = gways.Distinct(x => x.UserId).Count();
            eb.WithTitle("Giveaway Statistics!");
            eb.AddField("Amount of users that started giveaways", amount, true);
            eb.AddField("Total amount of giveaways", gways.Count, true);
            eb.AddField("Active Giveaways", gways.Count(x => x.Ended == 0), true);
            eb.AddField("Ended Giveaways", gways.Count(x => x.Ended == 1), true);
            eb.AddField("Giveaway Channels: Uses",
                string.Join("\n", gchans.Select(x => $"{x.Mention}: {gways.Count(s => s.ChannelId == x.Id)}")),
                true);

            await ctx.Channel.SendMessageAsync(embed: eb.Build());
        }
    }

    [MewdekoCommand, Usage, Description, Aliases, UserPerm(GuildPermission.ManageMessages)]
    public async Task GStart(ITextChannel chan, StoopidTime time, int winners, [Remainder] string what)
    {
        var emote = Service.GetGiveawayEmote(ctx.Guild.Id).ToIEmote();
        try
        {
            await ctx.Message.AddReactionAsync(emote);
        }
        catch
        {
            await ctx.Channel.SendErrorAsync(
                "The current giveaway emote is invalid or I can't access it! Please set it again and start a new giveaway.");
            return;
        }
        var user = await ctx.Guild.GetUserAsync(ctx.Client.CurrentUser.Id);
        var perms = user.GetPermissions(chan);
        if (!perms.Has(ChannelPermission.AddReactions))
        {
            await ctx.Channel.SendErrorAsync("I cannot add reactions in that channel!");
            return;
        }

        if (!perms.Has(ChannelPermission.UseExternalEmojis) && !ctx.Guild.Emotes.Contains(emote))
        {
            await ctx.Channel.SendErrorAsync("I'm unable to use external emotes!");
            return;
        }
        await Service.GiveawaysInternal(chan, time.Time, what, winners, ctx.User.Id, ctx.Guild.Id,
            ctx.Channel as ITextChannel, ctx.Guild);
    }

    [MewdekoCommand, Usage, Description, Aliases, UserPerm(GuildPermission.ManageMessages)]
    public async Task GStart()
    {
        var emote = Service.GetGiveawayEmote(ctx.Guild.Id).ToIEmote();
        try
        {
            await ctx.Message.AddReactionAsync(emote);
        }
        catch
        {
            await ctx.Channel.SendErrorAsync(
                "The current giveaway emote is invalid or I can't access it! Please set it again and start a new giveaway.");
            return;
        }
        ITextChannel chan = null;
        var winners = 0;
        string prize;
        //string blacklistroles;
        //string blacklistusers;
        string reqroles;
        IUser host;
        TimeSpan time;
        var erorrembed = new EmbedBuilder()
            .WithErrorColor()
            .WithDescription("Either something went wrong or you input a value incorrectly! Please start over.")
            .Build();
        var win0Embed = new EmbedBuilder()
            .WithErrorColor()
            .WithDescription("You can't have 0 winners!").Build();
        var tries = 0;
        var eb = new EmbedBuilder()
            .WithOkColor()
            .WithDescription(
                "Please say, mention or put the ID of the channel where you want to start a giveaway. (Keep in mind you can cancel this by just leaving this to sit)");
        var msg = await ctx.Channel.SendMessageAsync(embed: eb.Build());
        var next = await NextMessageAsync(ctx.Channel.Id, ctx.User.Id);
        var reader = new ChannelTypeReader<ITextChannel>();
        var e = await reader.ReadAsync(ctx, next, _servs);
        if (!e.IsSuccess)
        {
            await msg.ModifyAsync(x => x.Embed = erorrembed);
            return;
        }
        chan = (ITextChannel) e.BestMatch;
        var user = await ctx.Guild.GetUserAsync(ctx.Client.CurrentUser.Id);
        var perms = user.GetPermissions(chan);
        if (!perms.Has(ChannelPermission.AddReactions))
        {
            await ctx.Channel.SendErrorAsync("I cannot add reactions in that channel!");
            return;
        }

        if (!perms.Has(ChannelPermission.UseExternalEmojis) && !ctx.Guild.Emotes.Contains(emote))
        {
            await ctx.Channel.SendErrorAsync("I'm unable to use external emotes!");
            return;
        }
        await msg.ModifyAsync(x => x.Embed = eb.WithDescription("How many winners will there be?").Build());
        next = await NextMessageAsync(ctx.Channel.Id, ctx.User.Id);
        try
        {
            winners = int.Parse(next);
        }
        catch
        {
            await msg.ModifyAsync(x => x.Embed = erorrembed);
            tries++;
            return;
        }

        while (tries > 0)
        {
            await msg.ModifyAsync(x => x.Embed = win0Embed);
            next = await NextMessageAsync(ctx.Channel.Id, ctx.User.Id);
            try
            {
                winners = int.Parse(next);
                tries = 0;
            }
            catch
            {
                await msg.ModifyAsync(x => x.Embed = erorrembed);
                return;
            }
        }

        await msg.ModifyAsync(x => x.Embed = eb.WithDescription("What is the prize/item?").Build());
        prize = await NextMessageAsync(ctx.Channel.Id, ctx.User.Id);
        await msg.ModifyAsync(x =>
            x.Embed = eb.WithDescription("How long will this giveaway last? Use the format 1mo,2d,3m,4s").Build());
        next = await NextMessageAsync(ctx.Channel.Id, ctx.User.Id);
        {
            try
            {
                var t = StoopidTime.FromInput(next);
                time = t.Time;
            }
            catch
            {
                await msg.ModifyAsync(x => x.Embed = erorrembed);
                return;
            }
        }
        await msg.ModifyAsync(x =>
            x.Embed = eb
                .WithDescription(
                    "Who is the giveaway host? You can mention them or provide an ID, say none/skip to set yourself as the host.")
                .Build());
        next = await NextMessageAsync(ctx.Channel.Id, ctx.User.Id);
        if (next.ToLower() is "none" or "skip")
        {
            host = ctx.User;
        }
        else
        {
            var reader1 = new UserTypeReader<IUser>();
            try
            {
                var result = await reader1.ReadAsync(ctx, next, _servs);
                host = (IUser) result.BestMatch;
            }
            catch
            {
                await msg.ModifyAsync(x => x.Embed = erorrembed);
                return;
            }
        }
        
        if (!await PromptUserConfirmAsync(msg, new EmbedBuilder().WithDescription("Would you like to setup role requirements?").WithOkColor(), ctx.User.Id))
        {
            await Service.GiveawaysInternal(chan, time, prize, winners, host.Id, ctx.Guild.Id, ctx.Channel as ITextChannel,
            ctx.Guild);
            await msg.DeleteAsync();
        }
        await msg.ModifyAsync(x =>
        {
            x.Embed = eb.WithDescription("Alright! please mention the role(s) that are required for this giveaway!")
                        .Build();
            x.Components = null;
        });
        IReadOnlyCollection<IRole> parsed;
        while (true)
        {
            next = await NextMessageAsync(ctx.Channel.Id, ctx.User.Id);
            parsed = Regex.Matches(next, @"(?<=<@&)?[0-9]{17,19}(?=>)?")
                          .Select(m => ulong.Parse(m.Value))
                          .Select(Context.Guild.GetRole).OfType<IRole>().ToList();
            if (parsed.Any()) break;
            await msg.ModifyAsync(x => x.Embed = eb
                                                 .WithDescription("Looks like those roles were incorrect! Please try again!")
                                                 .Build());
        }

        reqroles = string.Join(" ", parsed.Select(x => x.Id));
        await msg.DeleteAsync();
        await Service.GiveawaysInternal(chan, time, prize, winners, host.Id, ctx.Guild.Id, ctx.Channel as ITextChannel,
            ctx.Guild, reqroles);
    }

    [MewdekoCommand, Usage, Description, Aliases, UserPerm(GuildPermission.ManageMessages)]
    public async Task GList()
    {
        await using var uow = _db.GetDbContext();
        var gways = uow.Giveaways.GiveawaysForGuild(ctx.Guild.Id).Where(x => x.Ended == 0);
        if (!gways.Any())
        {
            await ctx.Channel.SendErrorAsync("No active giveaways");
            return;
        }
        var paginator = new LazyPaginatorBuilder()
                        .AddUser(ctx.User)
                        .WithPageFactory(PageFactory)
                        .WithFooter(PaginatorFooter.PageNumber | PaginatorFooter.Users)
                        .WithMaxPageIndex(gways.Count() / 5)
                        .WithDefaultEmotes()
                        .Build();

        await _interactivity.SendPaginatorAsync(paginator, Context.Channel,
            TimeSpan.FromMinutes(60));

        async Task<PageBuilder> PageFactory(int page)
        {
            return new PageBuilder().WithOkColor().WithTitle($"{gways.Count()} Active Giveaways")
                                                    .WithDescription(string.Join("\n\n",
                                                        await gways.Skip(page * 5).Take(5).Select(async x =>
                                                            $"{x.MessageId}\nPrize: {x.Item}\nWinners: {x.Winners}\nLink: {await GetJumpUrl(x.ChannelId, x.MessageId)}").GetResults()));
        }
    }
    private async Task<string> GetJumpUrl(ulong channelId, ulong messageId)
    {
        var channel = await ctx.Guild.GetTextChannelAsync(channelId);
        var message = await channel.GetMessageAsync(messageId);
        return message.GetJumpUrl();
    }
    [MewdekoCommand, Aliases, RequireContext(ContextType.Guild), UserPerm(GuildPermission.ManageMessages)]
    public async Task GEnd(ulong messageid)
    {
        await using var uow = _db.GetDbContext();
        var gway = uow.Giveaways
                       .GiveawaysForGuild(ctx.Guild.Id).ToList().FirstOrDefault(x => x.MessageId == messageid);
        if (gway is null)
        {
            await ctx.Channel.SendErrorAsync("No Giveaway with that message ID exists! Please try again!");
        }

        if (gway.Ended == 1)
        {
            await ctx.Channel.SendErrorAsync(
                $"This giveaway has already ended! Plase use `{Prefix}greroll {messageid}` to reroll!");
        }
        else
        {
            await Service.GiveawayReroll(gway);
            await ctx.Channel.SendConfirmAsync("Giveaway ended!");
        }
        
    }
}