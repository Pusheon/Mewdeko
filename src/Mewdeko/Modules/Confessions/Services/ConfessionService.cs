﻿using System.Threading.Tasks;

namespace Mewdeko.Modules.Confessions.Services;

public class ConfessionService : INService
{
    private readonly DbService _db;
    private readonly DiscordSocketClient _client;
    private readonly Mewdeko _bot;
    private readonly GuildSettingsService _guildSettings;
    public ConfessionService(DbService db, Mewdeko bot, DiscordSocketClient client,
        GuildSettingsService guildSettings)
    {
        _db = db;
        _bot = bot;
        _client = client;
        _guildSettings = guildSettings;
    }

    public async Task SendConfession(
        ulong serverId,
        IUser user,
        string confession,
        IMessageChannel currentChannel, IInteractionContext? ctx = null, string? imageUrl = null)
    {
        var uow = _db.GetDbContext();
        var confessions = uow.Confessions.ForGuild(serverId);
        if (confessions.Count > 0)
        {
            var guild = _client.GetGuild(serverId);
            var current = confessions.LastOrDefault();
            var currentUser = guild.GetUser(_client.CurrentUser.Id);
            var confessionChannel = guild.GetTextChannel(_guildSettings.GetGuildConfig(serverId).ConfessionChannel);
            if (confessionChannel is null)
            {
                if (ctx is not null)
                {
                    await ctx.Interaction.SendEphemeralErrorAsync(
                        "The confession channel is invalid! Please tell the server staff about this!");
                    return;
                }
                await currentChannel.SendErrorAsync(
                    "The confession channel is invalid! Please tell the server staff about this!");
                return;
            }

            var eb = new EmbedBuilder().WithOkColor()
                                       .WithAuthor($"Anonymous confession #{current.ConfessNumber + 1}", guild.IconUrl)
                                       .WithDescription(confession)
                                       .WithFooter(
                                           $"Do /confess or dm me .confess {guild.Id} yourconfession to send a confession!")
                                       .WithCurrentTimestamp();
            if (imageUrl != null)
                eb.WithImageUrl(imageUrl);
            var perms = currentUser.GetPermissions(confessionChannel);
            if (!perms.EmbedLinks || !perms.SendMessages)
            {
                if (ctx is not null)
                {
                    await ctx.Interaction.SendEphemeralErrorAsync(
                        "Seems I dont have permission to post in the confession channel! Please tell the server staff.");
                    return;
                }
                await currentChannel.SendErrorAsync(
                    "Seems I dont have permission to post in the confession channel! Please tell the server staff.");
                return;
            }

            var msg = await confessionChannel.SendMessageAsync(embed: eb.Build());
            if (ctx is not null)
            {
                await ctx.Interaction.SendEphemeralConfirmAsync("Your confession has been sent! Please keep in mind if the server is abusing confessions you can send in a report using `/confessions report`");
            }
            else
            {
                await currentChannel.SendConfirmAsync(
                    "Your confession has been sent! Please keep in mind if the server is abusing confessions you can send in a report using `/confessions report`");
            }
            var toadd = new Database.Models.Confessions
            {
                ChannelId = current.ChannelId,
                Confession = confession,
                ConfessNumber = current.ConfessNumber + 1,
                GuildId = current.GuildId,
                MessageId = msg.Id,
                UserId = user.Id
            };
            uow.Confessions.Add(toadd);
            await uow.SaveChangesAsync().ConfigureAwait(false);
            if (GetConfessionLogChannel(serverId) != 0)
            {
                var logChannel = guild.GetTextChannel(GetConfessionLogChannel(serverId));
                if (logChannel is null)
                    return;
                var eb2 = new EmbedBuilder().WithErrorColor()
                                            .AddField("User", $"{user} | {user.Id}")
                                            .AddField($"Confession {current.ConfessNumber + 1}", confession)
                                            .AddField("Message Link", msg.GetJumpUrl()).AddField("***WARNING***",
                                                "***Misuse of this function will lead me to finding out, blacklisting this server, and tearing out your reproductive organs.***");
                await logChannel.SendMessageAsync(embed: eb2.Build());
            }
        }
        else
        {
            var guild = _client.GetGuild(serverId);
            var currentUser = guild.GetUser(_client.CurrentUser.Id);
            var confessionChannel = guild.GetTextChannel(GetConfessionChannel(guild.Id));
            if (confessionChannel is null)
            {
                if (ctx is not null)
                {
                    await ctx.Interaction.SendEphemeralErrorAsync(
                        "The confession channel is invalid! Please tell the server staff about this!");
                    return;
                }
                await currentChannel.SendErrorAsync(
                    "The confession channel is invalid! Please tell the server staff about this!");
                return;
            }

            var eb = new EmbedBuilder().WithOkColor()
                                       .WithAuthor("Anonymous confession #1", guild.IconUrl)
                                       .WithDescription(confession)
                                       .WithFooter(
                                           $"Do /confess or dm me .confess {guild.Id} yourconfession to send a confession!")
                                       .WithCurrentTimestamp();
            if (imageUrl != null)
                eb.WithImageUrl(imageUrl);
            var perms = currentUser.GetPermissions(confessionChannel);
            if (!perms.EmbedLinks || !perms.SendMessages)
            {
                if (ctx is not null)
                {
                    await ctx.Interaction.SendEphemeralErrorAsync(
                        "Seems I dont have permission to post in the confession channel! Please tell the server staff.");
                    return;
                }
                await currentChannel.SendErrorAsync(
                    "Seems I dont have permission to post in the confession channel! Please tell the server staff.");
                return;
            }

            var msg = await confessionChannel.SendMessageAsync(embed: eb.Build());
            if (ctx is not null)
            {
                await ctx.Interaction.SendEphemeralConfirmAsync("Your confession has been sent! Please keep in mind if the server is abusing confessions you can send in a report using `/confessions report`");
            }
            else
            {
                await currentChannel.SendConfirmAsync(
                    "Your confession has been sent! Please keep in mind if the server is abusing confessions you can send in a report using `/confessions report`");
            }
            var toadd = new Database.Models.Confessions
            {
                ChannelId = confessionChannel.Id,
                Confession = confession,
                ConfessNumber = 1,
                GuildId = guild.Id,
                MessageId = msg.Id,
                UserId = user.Id
            };
            uow.Confessions.Add(toadd);
            await uow.SaveChangesAsync().ConfigureAwait(false);
            if (GetConfessionLogChannel(serverId) != 0)
            {
                var logChannel = guild.GetTextChannel(GetConfessionLogChannel(serverId));
                if (logChannel is null)
                    return;
                var eb2 = new EmbedBuilder().WithErrorColor()
                                            .AddField("User", $"{user} | {user.Id}")
                                            .AddField("Confession 1", confession)
                                            .AddField("Message Link", msg.GetJumpUrl()).AddField("***WARNING***",
                                                "***Misuse of this function will lead me to finding out, blacklisting this server, and tearing out your reproductive organs.***");
                await logChannel.SendMessageAsync(embed: eb2.Build());
            }
        }
    }

    public async Task SetConfessionChannel(IGuild guild, ulong channelId)
    {
        await using var uow = _db.GetDbContext();
        var gc = uow.ForGuildId(guild.Id, set => set);
        gc.ConfessionChannel = channelId;
        await uow.SaveChangesAsync().ConfigureAwait(false);
        _guildSettings.UpdateGuildConfig(guild.Id, gc);
    }

    public ulong GetConfessionChannel(ulong id)
        => _guildSettings.GetGuildConfig(id).ConfessionChannel;

    public async Task ToggleUserBlacklistAsync(ulong guildId, ulong roleId)
    {
        await using var uow = _db.GetDbContext();
        var gc = uow.ForGuildId(guildId, set => set);
        var blacklists = gc.GetConfessionBlacklists();
        if (!blacklists.Remove(roleId))
            blacklists.Add(roleId);

        gc.SetConfessionBlacklists(blacklists);
        await uow.SaveChangesAsync().ConfigureAwait(false);
        _guildSettings.UpdateGuildConfig(guildId, gc);
    }

    public async Task SetConfessionLogChannel(IGuild guild, ulong channelId)
    {
        await using var uow = _db.GetDbContext();
        var gc = uow.ForGuildId(guild.Id, set => set);
        gc.ConfessionLogChannel = channelId;
        await uow.SaveChangesAsync().ConfigureAwait(false);
        _guildSettings.UpdateGuildConfig(guild.Id, gc);
    }

    public ulong GetConfessionLogChannel(ulong id)
        => _guildSettings.GetGuildConfig(id).ConfessionLogChannel;
}

public static class ConfessionExtensions
{
    public static List<ulong> GetConfessionBlacklists(this GuildConfig gc)
        => string.IsNullOrWhiteSpace(gc.ConfessionBlacklist) ? new List<ulong>() : gc.ConfessionBlacklist.Split(' ').Select(ulong.Parse).ToList();

    public static void SetConfessionBlacklists(this GuildConfig gc, IEnumerable<ulong> blacklists) =>
        gc.ConfessionBlacklist = blacklists.JoinWith(' ');
}