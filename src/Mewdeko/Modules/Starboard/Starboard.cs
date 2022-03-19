using Discord;
using Discord.Commands;
using Mewdeko._Extensions;
using Mewdeko.Common;
using Mewdeko.Common.Attributes;
using Mewdeko.Modules.Starboard.Services;

namespace Mewdeko.Modules.Starboard;

public enum StarboardSetting
{
    UseChannelBlacklist,
    Star,
    Starcount,
}
public class Starboard : MewdekoSubmodule<StarboardService>
{
    [MewdekoCommand, Usage, Description, Aliases, UserPerm(GuildPermission.ManageChannels)]
    public async Task SetStarboard(ITextChannel? chn = null)
    {
        if (chn is null)
        {
            await Service.SetStarboardChannel(ctx.Guild, 0);
            await ctx.Channel.SendMessageAsync("Starboard has been disabled.");
            return;
        }

        await Service.SetStarboardChannel(ctx.Guild, chn.Id);
        await ctx.Channel.SendConfirmAsync($"Channel set to {chn.Mention}");
    }

    [MewdekoCommand, Usage, Description, Alias, UserPerm(GuildPermission.ManageChannels)]
    public async Task SetRepostThreshold(int num)
    {
        if (num == 0)
        {
            await ctx.Channel.SendErrorAsync("Reposting has been disabled!");
            await Service.SetRepostThreshold(ctx.Guild, 0);
            return;
        }
        await Service.SetRepostThreshold(ctx.Guild, num);
        await ctx.Channel.SendConfirmAsync($"Successfully set the Repost Threshold to {num}");
    }

    [MewdekoCommand, Usage, Description, Alias, UserPerm(GuildPermission.ManageChannels)]
    public async Task SetStars(int num)
    {
        var count = Service.GetStarCount(ctx.Guild.Id);
        await Service.SetStarCount(ctx.Guild, num);
        var count2 = Service.GetStarCount(ctx.Guild.Id);
        await ctx.Channel.SendConfirmAsync($"Your star count was successfully changed from {count} to {count2}!");
    }

    [MewdekoCommand, Usage, Description, Alias, UserPerm(GuildPermission.ManageChannels)]
    public async Task SetStar(IEmote? emote = null)
    {
        if (emote is null)
        {
            var maybeEmote = Service.GetStar(ctx.Guild.Id).ToIEmote();
            if (maybeEmote.Name is null)
            {
                await ctx.Channel.SendErrorAsync("You don't have an emote set!");
                return;
            }

            await ctx.Channel.SendConfirmAsync(
                $"Your current starboard emote is {maybeEmote} {Format.Code(maybeEmote.ToString())}");
            return;

        }

        try
        {
            await ctx.Message.AddReactionAsync(emote);
        }
        catch
        {
            await ctx.Channel.SendErrorAsync("I'm unable to use that emote! Please use a different one.");
            return;
        }
        await Service.SetStar(ctx.Guild, emote.ToString());
        await ctx.Channel.SendConfirmAsync($"Successfully set the star to {emote}");
    }

    [MewdekoCommand, Description, Aliases]
    public async Task StarboardWlMode(bool enabled)
    {
        await Service.SetCheckMode(ctx.Guild, enabled);
        if (enabled)
        {
            await ctx.Channel.SendConfirmAsync("Starboard will now use whitelist mode!");
            return;
        }
        await ctx.Channel.SendConfirmAsync("Starboard will now use blacklist mode!");
    }

    [MewdekoCommand, Description, Aliases]
    public async Task StarboardChToggle(ITextChannel channel)
    {
        if (!await Service.ToggleChannel(ctx.Guild, channel.Id.ToString()))
        {
            await ctx.Channel.SendConfirmAsync($"{channel.Mention} Has been added to the whitelist/blacklist (Depnding on what was set in `{Prefix}swm`)");
            return;
        }
        await ctx.Channel.SendConfirmAsync($"{channel.Mention} Has been removed from the whitelist/blacklist (Depnding on what was set in `{Prefix}swm`)");
    }
}