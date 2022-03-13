using Discord;
using Discord.WebSocket;
using Mewdeko.Common.ModuleBehaviors;
using Mewdeko.Common.PubSub;
using Mewdeko.Database;
using Mewdeko.Database.Extensions;
using Mewdeko.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace Mewdeko.Modules.Permissions.Services;

public sealed class BlacklistService : IEarlyBehavior, INService
{
    private readonly DbService _db;
    private readonly IPubSub _pubSub;

    private readonly TypedKey<BlacklistEntry[]> _blPubKey = new("blacklist.reload");
    public IReadOnlyList<BlacklistEntry> blacklist;

    public BlacklistService(DbService db, IPubSub pubSub)
    {
        _db = db;
        _pubSub = pubSub;

        Reload(false);
        _pubSub.Sub(_blPubKey, OnReload);
    }

    public int Priority => -100;

    public ModuleBehaviorType BehaviorType => ModuleBehaviorType.Blocker;

    public Task<bool> RunBehavior(DiscordSocketClient _, IGuild guild, IUserMessage usrMsg)
    {
        foreach (var bl in blacklist)
        {
            if (guild != null && bl.Type == BlacklistType.Server && bl.ItemId == guild.Id)
                return Task.FromResult(true);

            switch (bl.Type)
            {
                case BlacklistType.Channel when bl.ItemId == usrMsg.Channel.Id:
                    return Task.FromResult(true);
                case BlacklistType.User when bl.ItemId == usrMsg.Author.Id:
                    return Task.FromResult(true);
            }
        }

        return Task.FromResult(false);
    }

    private ValueTask OnReload(BlacklistEntry[] blacklist)
    {
        this.blacklist = blacklist;
        return default;
    }

    public void Reload(bool publish = true)
    {
        using var uow = _db.GetDbContext();
        var toPublish = uow.Blacklist.AsNoTracking().ToArray();
        blacklist = toPublish;
        if (publish) _pubSub.Pub(_blPubKey, toPublish);
    }

    public void Blacklist(BlacklistType type, ulong id)
    {
        using var uow = _db.GetDbContext();
        var item = new BlacklistEntry {ItemId = id, Type = type};
        uow.Blacklist.Add(item);
        uow.SaveChanges();

        Reload();
    }

    public void UnBlacklist(BlacklistType type, ulong id)
    {
        using var uow = _db.GetDbContext();
        var toRemove = uow.Blacklist
            .FirstOrDefault(bi => bi.ItemId == id && bi.Type == type);

        if (toRemove is not null)
           uow.Blacklist.Remove(toRemove);

        uow.SaveChanges();

        Reload();
    }

    public void BlacklistUsers(IReadOnlyCollection<ulong> toBlacklist)
    {
        using (var uow = _db.GetDbContext())
        {
            var bc = uow.Blacklist;
            //blacklist the users
            bc.AddRange(toBlacklist.Select(x =>
                new BlacklistEntry
                {
                    ItemId = x,
                    Type = BlacklistType.User
                }));

            //clear their currencies
            uow.DiscordUser.RemoveFromMany(toBlacklist);
            uow.SaveChanges();
        }

        Reload();
    }
}