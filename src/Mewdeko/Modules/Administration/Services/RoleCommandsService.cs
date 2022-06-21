﻿using Mewdeko.Database.Common;
using Microsoft.EntityFrameworkCore;
using Serilog;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace Mewdeko.Modules.Administration.Services;

public class RoleCommandsService : INService
{
    private readonly DbService _db;
    private readonly ConcurrentDictionary<ulong, IndexedCollection<ReactionRoleMessage>> _models;

    public RoleCommandsService(DiscordSocketClient client, DbService db, Mewdeko bot)
    {
        _db = db;
        using var uow = db.GetDbContext();
        var gc = uow.GuildConfigs.All().Where(x => bot.GetCurrentGuildIds().Contains(x.GuildId));
        _models = gc.ToDictionary(x => x.GuildId,
                x => x.ReactionRoleMessages)
            .ToConcurrent();
        client.ReactionAdded += _client_ReactionAdded;
        client.ReactionRemoved += _client_ReactionRemoved;
    }

    private Task _client_ReactionAdded(Cacheable<IUserMessage, ulong> msg, Cacheable<IMessageChannel, ulong> chan,
        SocketReaction reaction)
    {
        _ = Task.Factory.StartNew(async () =>
        {
            try
            {
                await msg.DownloadAsync().ConfigureAwait(false);
                if (!reaction.User.IsSpecified ||
                    reaction.User.Value.IsBot ||
                    reaction.User.Value is not SocketGuildUser gusr)
                {
                    return;
                }

                if (chan.Value is not SocketGuildChannel gch)
                    return;

                if (!_models.TryGetValue(gch.Guild.Id, out var confs))
                    return;

                var conf = confs.FirstOrDefault(x => x.MessageId == msg.Id);

                if (conf == null)
                    return;

                // compare emote names for backwards compatibility :facepalm:
                var reactionRole = conf.ReactionRoles.Find(x =>
                    x.EmoteName == reaction.Emote.Name || x.EmoteName == reaction.Emote.ToString());
                if (reactionRole != null)
                {
                    if (conf.Exclusive)
                    {
                        var roleIds = conf.ReactionRoles.Select(x => x.RoleId)
                            .Where(x => x != reactionRole.RoleId)
                            .Select(x => gusr.Guild.GetRole(x))
                            .Where(x => x != null);

                        var __ = Task.Factory.StartNew(async () =>
                        {
                            try
                            {
                                //if the role is exclusive,
                                // remove all other reactions user added to the message
                                var dl = await msg.GetOrDownloadAsync().ConfigureAwait(false);
                                foreach (var (key, _) in dl.Reactions)
                                {
                                    if (key.Name == reaction.Emote.Name)
                                        continue;
                                    try
                                    {
                                        await dl.RemoveReactionAsync(key, gusr).ConfigureAwait(false);
                                    }
                                    catch
                                    {
                                        // ignored
                                    }

                                    await Task.Delay(100).ConfigureAwait(false);
                                }
                            }
                            catch
                            {
                                // ignored
                            }
                        });
                        await gusr.RemoveRolesAsync(roleIds).ConfigureAwait(false);
                    }

                    var toAdd = gusr.Guild.GetRole(reactionRole.RoleId);
                    if (toAdd != null && !gusr.Roles.Contains(toAdd))
                        await gusr.AddRolesAsync(new[] { toAdd }).ConfigureAwait(false);
                }
                else
                {
                    var dl = await msg.GetOrDownloadAsync().ConfigureAwait(false);
                    await dl.RemoveReactionAsync(reaction.Emote, dl.Author,
                        new RequestOptions
                        {
                            RetryMode = RetryMode.RetryRatelimit | RetryMode.Retry502
                        }).ConfigureAwait(false);
                    Log.Warning("User {0} is adding unrelated reactions to the reaction roles message.", dl.Author);
                }
            }
            catch
            {
                // ignored
            }
        }, TaskCreationOptions.LongRunning);

        return Task.CompletedTask;
    }

    private Task _client_ReactionRemoved(Cacheable<IUserMessage, ulong> msg, Cacheable<IMessageChannel, ulong> chan,
        SocketReaction reaction)
    {
        _ = Task.Factory.StartNew(async () =>
        {
            try
            {
                if (!reaction.User.IsSpecified ||
                    reaction.User.Value.IsBot ||
                    reaction.User.Value is not SocketGuildUser gusr)
                {
                    return;
                }

                if (chan.Value is not SocketGuildChannel gch)
                    return;

                if (!_models.TryGetValue(gch.Guild.Id, out var confs))
                    return;
                var message = await msg.GetOrDownloadAsync();
                var conf = confs.FirstOrDefault(x => x.MessageId == message.Id);

                if (conf == null)
                    return;

                var reactionRole = conf.ReactionRoles.Find(x =>
                    x.EmoteName == reaction.Emote.Name || x.EmoteName == reaction.Emote.ToString());

                if (reactionRole != null)
                {
                    var role = gusr.Guild.GetRole(reactionRole.RoleId);
                    if (role == null)
                        return;
                    await gusr.RemoveRoleAsync(role).ConfigureAwait(false);
                }
            }
            catch
            {
                // ignored
            }
        }, TaskCreationOptions.LongRunning);
        return Task.CompletedTask;
    }

    public bool Get(ulong id, out IndexedCollection<ReactionRoleMessage> rrs) => _models.TryGetValue(id, out rrs);

    public bool Add(ulong id, ReactionRoleMessage rrm)
    {
        using var uow = _db.GetDbContext();
        var gc = uow.ForGuildId(id, set => set
            .Include(x => x.ReactionRoleMessages)
            .ThenInclude(x => x.ReactionRoles));
        gc.ReactionRoleMessages.Add(rrm);
        _models.AddOrUpdate(id,
            gc.ReactionRoleMessages,
            delegate { return gc.ReactionRoleMessages; });
        uow.SaveChanges();

        return true;
    }

    public void Remove(ulong id, int index)
    {
        using var uow = _db.GetDbContext();
        var gc = uow.ForGuildId(id,
            set => set.Include(x => x.ReactionRoleMessages)
                .ThenInclude(x => x.ReactionRoles));
        uow.Set<ReactionRole>()
            .RemoveRange(gc.ReactionRoleMessages[index].ReactionRoles);
        gc.ReactionRoleMessages.RemoveAt(index);
        _models.AddOrUpdate(id,
            gc.ReactionRoleMessages,
            delegate { return gc.ReactionRoleMessages; });
        uow.SaveChanges();
    }
}