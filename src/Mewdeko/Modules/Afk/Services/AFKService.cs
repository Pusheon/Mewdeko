using System.Collections.Concurrent;
using System.Collections.Generic;
using Discord;
using Discord.WebSocket;
using Humanizer;
using Mewdeko._Extensions;
using Mewdeko.Common;
using Mewdeko.Common.Replacements;
using Mewdeko.Database.Extensions;
using Mewdeko.Database.Models;

namespace Mewdeko.Modules.Afk.Services;

public class AfkService : INService
{
    private readonly DbService _db;
    private readonly CommandHandler _cmd;
    public DiscordSocketClient Client;
    public ConcurrentDictionary<ulong, List<AFK>> CachedAfk = new();
    private Mewdeko _bot;

    public AfkService(
        DbService db,
        DiscordSocketClient client,
        CommandHandler handle,
        Mewdeko bot)
    {
        _bot = bot;
        _db = db;
        Client = client;
        _cmd = handle;
        AfkType = bot.AllGuildConfigs.ToDictionary(x => x.GuildId, x => x.AfkType).ToConcurrent();
        AfkTimeout = bot.AllGuildConfigs.ToDictionary(x => x.GuildId, x => x.AfkTimeout).ToConcurrent();
        AfkDisabledChannels = bot.AllGuildConfigs
                                 .ToDictionary(x => x.GuildId, x => x.AfkDisabledChannels).ToConcurrent();
        AfkLengths = bot.AllGuildConfigs.ToDictionary(x => x.GuildId, x => x.AfkLength).ToConcurrent();
        AfkMessage = bot.AllGuildConfigs.ToDictionary(x => x.GuildId, x => x.AfkMessage).ToConcurrent();
        AfkDels = bot.AllGuildConfigs.ToDictionary(x => x.GuildId, x => x.AfkDel).ToConcurrent();
        Client.MessageReceived += MessageReceived;
        Client.MessageUpdated += MessageUpdated;
        Client.UserIsTyping += UserTyping;
        _ = CacheAfk();
    }

    private ConcurrentDictionary<ulong, int> AfkType { get; }
    private ConcurrentDictionary<ulong, string> AfkMessage { get; }
    private ConcurrentDictionary<ulong, int> AfkTimeout { get; }
    private ConcurrentDictionary<ulong, int> AfkLengths { get; }
    private ConcurrentDictionary<ulong, string> AfkDisabledChannels { get; }
    private ConcurrentDictionary<ulong, int> AfkDels { get; }

    private async Task CacheAfk()
    {
        {
            var uow = _db.GetDbContext();
            foreach (var i in _bot.AllGuildConfigs.Select(x => x.GuildId))
            {
                var addto = CachedAfk.GetOrAdd(i, new List<AFK>());
                addto.AddRange(uow.Afk.ForGuild(i));
            }
        }
    }
    

    private Task UserTyping(Cacheable<IUser, ulong> user, Cacheable<IMessageChannel, ulong> chan)
    {
        _ = Task.Run(async () =>
        {
            if (user.Value is IGuildUser use)
                if (GetAfkType(use.GuildId) is 2 or 4)
                    if (IsAfk(use.Guild, use))
                    {
                        var t = GetAfkMessage(use.Guild.Id, user.Id).Last();
                        if (t.DateAdded != null
                            && t.DateAdded.Value.ToLocalTime() < DateTime.Now.AddSeconds(-GetAfkTimeout(use.GuildId))
                            && t.WasTimed == 0)
                        {
                            await AfkSet(use.Guild, use, "", 0);
                            var msg = await chan.Value.SendMessageAsync(
                                $"Welcome back {user.Value.Mention}! I noticed you typing so I disabled your afk.");
                            try
                            {
                                await use.ModifyAsync(x => x.Nickname = use.Nickname.Replace("[AFK]", ""));
                            }
                            catch
                            {
                                //ignored
                            }

                            msg.DeleteAfter(5);
                        }
                    }
        });
        return Task.CompletedTask;
    }

    private Task MessageReceived(SocketMessage msg)
    {
        _ = Task.Run(async () =>
        {
            if (msg.Author.IsBot)
                return;

            if (msg.Author is IGuildUser user)
            {
                if (GetAfkType(user.Guild.Id) is 3 or 4)
                    if (IsAfk(user.Guild, user))
                    {
                        var t = GetAfkMessage(user.Guild.Id, user.Id).Last();
                        if (t.DateAdded != null
                            && t.DateAdded.Value.ToLocalTime() < DateTime.Now.AddSeconds(-GetAfkTimeout(user.GuildId))
                            && t.WasTimed == 0)
                        {
                            await AfkSet(user.Guild, user, "", 0);
                            var ms = await msg.Channel.SendMessageAsync(
                                $"Welcome back {user.Mention}, I have disabled your AFK for you.");
                            ms.DeleteAfter(5);
                            try
                            {
                                await user.ModifyAsync(x => x.Nickname = user.Nickname.Replace("[AFK]", ""));
                            }
                            catch
                            {
                                //ignored
                            }

                            return;
                        }
                    }

                if (msg.MentionedUsers.Count > 0 && !msg.Author.IsBot)
                {
                    if (msg.Content.Contains($"{_cmd.GetPrefix(user.Guild)}afkremove")
                        || msg.Content.Contains($"{_cmd.GetPrefix(user.Guild)}afkrm")
                        || msg.Content.Contains($"{_cmd.GetPrefix(user.Guild)}afk")) return;
                    if (GetDisabledAfkChannels(user.GuildId) is not "0" and not null)
                    {
                        var chans = GetDisabledAfkChannels(user.GuildId);
                        var e = chans.Split(",");
                        if (e.Contains(msg.Channel.Id.ToString())) return;
                    }

                    if (msg.MentionedUsers.FirstOrDefault() is not IGuildUser mentuser) return;
                    if (IsAfk(user.Guild, mentuser))
                    {
                        try
                        {
                            await user.ModifyAsync(x => x.Nickname = user.Nickname.Replace("[AFK]", ""));
                        }
                        catch
                        {
                            //ignored
                        }
                        var afkdel = GetAfkDel(((ITextChannel)msg.Channel).GuildId);
                        var replacer = new ReplacementBuilder()
                                       .WithOverride("%afk.message%",
                                           () => GetAfkMessage(user.GuildId, mentuser.Id).Last().Message
                                               .Truncate(GetAfkLength(user.GuildId)))
                                       .WithOverride("%afk.user%", () => mentuser.ToString())
                                       .WithOverride("%afk.user.mention%", () => mentuser.Mention)
                                       .WithOverride("%afk.user.avatar%", () => mentuser.GetAvatarUrl(size: 2048))
                                       .WithOverride("%afk.user.id%", () => mentuser.Id.ToString())
                                       .WithOverride("%afk.triggeruser%", () => msg.Author.ToString())
                                       .WithOverride("%afk.triggeruser.avatar%",
                                           () => msg.Author.RealAvatarUrl().ToString())
                                       .WithOverride("%afk.triggeruser.id%", () => msg.Author.Id.ToString())
                                       .WithOverride("%afk.triggeruser.mention%", () => msg.Author.Mention)
                                       .WithOverride("%afk.time%", () =>
                                           // ReSharper disable once PossibleInvalidOperationException
                                           $"{(DateTime.UtcNow - GetAfkMessage(user.GuildId, user.Id).Last().DateAdded.Value).Humanize()}")
                                       .Build();
                        var ebe = CrEmbed.TryParse(GetCustomAfkMessage(mentuser.GuildId), out var crEmbed);
                        if (!ebe)
                        {
                            await SetCustomAfkMessage(user.Guild, "-");
                            var a = await msg.Channel.EmbedAsync(new EmbedBuilder()
                                                                 .WithAuthor(eab =>
                                                                     eab.WithName($"{mentuser} is currently away")
                                                                        .WithIconUrl(mentuser.GetAvatarUrl()))
                                                                 .WithDescription(
                                                                     GetAfkMessage(user.GuildId, mentuser.Id).Last()
                                                                         .Message.Truncate(GetAfkLength(user.Guild.Id)))
                                                                 .WithFooter(new EmbedFooterBuilder
                                                                 {
                                                                     Text =
                                                                         $"AFK for {(DateTime.UtcNow - GetAfkMessage(user.GuildId, mentuser.Id).Last().DateAdded.Value).Humanize()}"
                                                                 }).WithOkColor());
                            if (afkdel != 0)
                                a.DeleteAfter(afkdel);
                            return;
                        }

                        replacer.Replace(crEmbed);
                        if (crEmbed.PlainText != null && crEmbed.IsEmbedValid)
                        {
                            var a = await msg.Channel.SendMessageAsync(crEmbed.PlainText.SanitizeAllMentions(),
                                embed: crEmbed.ToEmbed().Build());
                            if (afkdel != 0)
                                a.DeleteAfter(afkdel);
                            return;
                        }

                        if (crEmbed.PlainText is null)
                        {
                            var a = await msg.Channel.SendMessageAsync(embed: crEmbed.ToEmbed().Build());
                            if (afkdel != 0)
                                a.DeleteAfter(afkdel);
                            return;
                        }

                        if (crEmbed.PlainText != null && !crEmbed.IsEmbedValid)
                        {
                            var a = await msg.Channel.SendMessageAsync(crEmbed.PlainText.SanitizeAllMentions());
                            if (afkdel != 0)
                                a.DeleteAfter(afkdel);
                        }
                    }
                }
            }
        });
        return Task.CompletedTask;
    }

    public IEnumerable<IGuildUser> GetAfkUsers(IGuild guild) =>
        CachedAfk.Select(x => x.Key).Contains(guild.Id)
            ? new IGuildUser[] { }
            : CachedAfk.FirstOrDefault(x => x.Key == guild.Id).Value.GroupBy(m => m.UserId)
                       .Where(m => !string.IsNullOrEmpty(m.Last().Message))
                       .Select(m => guild.GetUserAsync(m.Key).Result);

    public async Task SetCustomAfkMessage(IGuild guild, string afkMessage)
    {
        await using (var uow = _db.GetDbContext())
        {
            var gc = uow.ForGuildId(guild.Id, set => set);
            gc.AfkMessage = afkMessage;
            await uow.SaveChangesAsync();
        }

        AfkMessage.AddOrUpdate(guild.Id, afkMessage, (_, _) => afkMessage);
    }

    public async Task TimedAfk(
        IGuild guild,
        IUser user,
        string message,
        TimeSpan time)
    {
        await AfkSet(guild, user as IGuildUser, message, 1);
        await Task.Delay(time.Milliseconds);
        await AfkSet(guild, user as IGuildUser, "", 0);
    }

    public bool IsAfk(IGuild guild, IGuildUser user)
    {
        var afkmsg = GetAfkMessage(guild.Id, user.Id);
        var result = afkmsg?.LastOrDefault()?.Message;
        return !string.IsNullOrEmpty(result);
    }

    private Task MessageUpdated(Cacheable<IMessage, ulong> msg, SocketMessage msg2, ISocketMessageChannel t)
    {
        if (msg.Value is not null && msg.Value.Content == msg2.Content) return Task.CompletedTask;
        return MessageReceived(msg2);
    }

    public async Task AfkTypeSet(IGuild guild, int num)
    {
        await using (var uow = _db.GetDbContext())
        {
            var gc = uow.ForGuildId(guild.Id, set => set);
            gc.AfkType = num;
            await uow.SaveChangesAsync();
        }

        AfkType.AddOrUpdate(guild.Id, num, (_, _) => num);
    }

    public async Task AfkDelSet(IGuild guild, int num)
    {
        await using (var uow = _db.GetDbContext())
        {
            var gc = uow.ForGuildId(guild.Id, set => set);
            gc.AfkDel = num;
            await uow.SaveChangesAsync();
        }

        AfkDels.AddOrUpdate(guild.Id, num, (_, _) => num);
    }

    public async Task AfkLengthSet(IGuild guild, int num)
    {
        await using (var uow = _db.GetDbContext())
        {
            var gc = uow.ForGuildId(guild.Id, set => set);
            gc.AfkLength = num;
            await uow.SaveChangesAsync();
        }

        AfkLengths.AddOrUpdate(guild.Id, num, (_, _) => num);
    }


    public async Task AfkTimeoutSet(IGuild guild, int num)
    {
        await using (var uow = _db.GetDbContext())
        {
            var gc = uow.ForGuildId(guild.Id, set => set);
            gc.AfkTimeout = num;
            await uow.SaveChangesAsync();
        }

        AfkTimeout.AddOrUpdate(guild.Id, num, (_, _) => num);
    }

    public async Task AfkDisabledSet(IGuild guild, string num)
    {
        await using (var uow = _db.GetDbContext())
        {
            var gc = uow.ForGuildId(guild.Id, set => set);
            gc.AfkDisabledChannels = num;
            await uow.SaveChangesAsync();
        }

        AfkDisabledChannels.AddOrUpdate(guild.Id, num, (_, _) => num);
    }

    public string GetCustomAfkMessage(ulong? id)
    {
        AfkMessage.TryGetValue(id.Value, out var snum);
        return snum;
    }

    public int GetAfkDel(ulong? id)
    {
        AfkDels.TryGetValue(id.Value, out var snum);
        return snum;
    }

    private int GetAfkType(ulong? id)
    {
        AfkType.TryGetValue(id.Value, out var snum);
        return snum;
    }

    public int GetAfkLength(ulong? id)
    {
        AfkLengths.TryGetValue(id.Value, out var snum);
        return snum;
    }

    public string GetDisabledAfkChannels(ulong? id)
    {
        AfkDisabledChannels.TryGetValue(id.Value, out var snum);
        return snum;
    }

    private int GetAfkTimeout(ulong? id)
    {
        AfkTimeout.TryGetValue(id.Value, out var snum);
        return snum;
    }

    public async Task AfkSet(
        IGuild guild,
        IGuildUser user,
        string message,
        int timed)
    {
        var afk = new AFK {GuildId = guild.Id, UserId = user.Id, Message = message, WasTimed = timed};
        await using var uow = _db.GetDbContext();
        uow.Afk.Update(afk);
        await uow.SaveChangesAsync();
        var toaddto = CachedAfk.GetOrAdd(guild.Id, new List<AFK>());
        toaddto.Add(afk);
    }

    public List<AFK> GetAfkMessage(ulong gid, ulong uid)
    {
        var e = CachedAfk.FirstOrDefault(x => x.Key == gid).Value.Where(x => x.UserId == uid).ToList();
        return e;
    }
}
