using Discord;
using Discord.WebSocket;
using Mewdeko._Extensions;
using Mewdeko.Common;
using Mewdeko.Common.ModuleBehaviors;
using Mewdeko.Common.PubSub;
using Mewdeko.Common.Yml;
using Mewdeko.Database;
using Mewdeko.Database.Extensions;
using Mewdeko.Database.Models;
using Mewdeko.Modules.Chat_Triggers.Common;
using Mewdeko.Modules.Chat_Triggers.Extensions;
using Mewdeko.Modules.Permissions.Common;
using Mewdeko.Modules.Permissions.Services;
using Mewdeko.Services.strings;
using Microsoft.EntityFrameworkCore;
using Serilog;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Mewdeko.Modules.Chat_Triggers.Services;

public sealed class ChatTriggersService : IEarlyBehavior, INService, IReadyExecutor
{
    public enum CtField
    {
        AutoDelete,
        DmResponse,
        AllowTarget,
        ContainsAnywhere,
        Message,
        ReactToTrigger,
        NoRespond
    }

    private const string MENTION_PH = "%bot.mention%";

    private const string PREPEND_EXPORT =
        @"# Keys are triggers, Each key has a LIST of custom reactions in the following format:
# - res: Response string
#   react: 
#     - <List
#     -  of
#     - reactions>
#   at: Whether custom reaction allows targets (see .h .crat) 
#   ca: Whether custom reaction expects trigger anywhere (see .h .crca) 
#   dm: Whether custom reaction DMs the response (see .h .crdm) 
#   ad: Whether custom reaction automatically deletes triggering message (see .h .crad) 
#   rtt: Whether custom reaction emotes are added to the response or trigger

";

    private static readonly ISerializer _exportSerializer = new SerializerBuilder()
        .WithEventEmitter(args => new MultilineScalarFlowStyleEmitter(args))
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .WithIndentedSequences()
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitDefaults)
        .DisableAliases()
        .Build();

    private readonly Mewdeko _bot;
    private readonly DiscordSocketClient _client;
    private readonly CommandHandler _cmd;
    private readonly CmdCdService _cmdCds;
    private readonly TypedKey<bool> _crsReloadedKey = new("crs.reloaded");

    private readonly DbService _db;

    private readonly TypedKey<Database.Models.ChatTriggers> _gcrAddedKey = new("gcr.added");
    private readonly TypedKey<int> _gcrDeletedkey = new("gcr.deleted");
    private readonly TypedKey<Database.Models.ChatTriggers> _gcrEditedKey = new("gcr.edited");

    private readonly object _gcrWriteLock = new();
    private readonly GlobalPermissionService _gperm;
    private readonly PermissionService _perms;
    private readonly IPubSub _pubSub;
    private readonly Random _rng;
    private readonly IBotStrings _strings;

    // it is perfectly fine to have global customreactions as an array
    // 1. custom reactions are almost never added (compared to how many times they are being looped through)
    // 2. only need write locks for this as we'll rebuild+replace the array on every edit
    // 3. there's never many of them (at most a thousand, usually < 100)
    private Database.Models.ChatTriggers[] globalReactions;
    private ConcurrentDictionary<ulong, Database.Models.ChatTriggers[]> newGuildReactions;

    private bool ready;

    public ChatTriggersService(PermissionService perms, DbService db, IBotStrings strings,
        Mewdeko bot,
        DiscordSocketClient client, CommandHandler cmd, GlobalPermissionService gperm, CmdCdService cmdCds,
        IPubSub pubSub)
    {
        _db = db;
        _client = client;
        _perms = perms;
        _cmd = cmd;
        _strings = strings;
        _bot = bot;
        _cmdCds = cmdCds;
        _gperm = gperm;
        _pubSub = pubSub;
        _rng = new MewdekoRandom();

        _pubSub.Sub(_crsReloadedKey, OnCrsShouldReload);
        pubSub.Sub(_gcrAddedKey, OnGcrAdded);
        pubSub.Sub(_gcrDeletedkey, OnGcrDeleted);
        pubSub.Sub(_gcrEditedKey, OnGcrEdited);

        bot.JoinedGuild += OnJoinedGuild;
        _client.LeftGuild += OnLeftGuild;
    }

    public int Priority => -1;
    public ModuleBehaviorType BehaviorType => ModuleBehaviorType.Executor;
    

    public async Task<bool> RunBehavior(DiscordSocketClient client, IGuild guild, IUserMessage msg)
    {
        // maybe this message is a custom reaction
        var cr = TryGetChatTriggers(msg);

        if (cr is null)
            return false;
        if (await _cmdCds.TryBlock(guild, msg.Author, cr.Trigger))
            return false;


        try
        {
            if (_gperm.BlockedModules.Contains("ActualChatTriggers")) return true;

            if (guild is SocketGuild sg)
            {
                var pc = _perms.GetCacheFor(guild.Id);
                if (!pc.Permissions.CheckPermissions(msg, cr.Trigger, "ActualChatTriggers",
                        out var index))
                {
                    if (pc.Verbose)
                    {
                        var returnMsg = _strings.GetText("perm_prevent", sg.Id,
                            index + 1,
                            Format.Bold(pc.Permissions[index].GetCommand(_cmd.GetPrefix(guild), sg)));
                        try
                        {
                            await msg.Channel.SendErrorAsync(returnMsg).ConfigureAwait(false);
                        }
                        catch
                        {
                        }

                        Log.Information(returnMsg);
                    }

                    return true;
                }
            }

            IUserMessage sentMsg = null;
            if (!cr.NoRespond) 
                sentMsg = await cr.Send(msg, _client, false).ConfigureAwait(false);

            var reactions = cr.GetReactions();
            foreach (var reaction in reactions)
            {
                try
                {
                    if (!cr.ReactToTrigger && !cr.NoRespond)
                        await sentMsg.AddReactionAsync(reaction.ToIEmote());
                    else
                        await msg.AddReactionAsync(reaction.ToIEmote());
                }
                catch
                {
                    Log.Warning("Unable to add reactions to message {Message} in server {GuildId}", sentMsg.Id,
                        cr.GuildId);
                    break;
                }

                await Task.Delay(1000);
            }

            if (!cr.AutoDeleteTrigger) return true;
            try
            {
                await msg.DeleteAsync().ConfigureAwait(false);
            }
            catch
            {
                // ignored
            }

            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex.Message);
        }

        return false;
    }

    public string ExportCrs(ulong? guildId)
    {
        var crs = GetChatTriggersFor(guildId);

        var crsDict = crs
            .GroupBy(x => x.Trigger)
            .ToDictionary(x => x.Key, x => x.Select(ExportedTriggers.FromModel));

        return PREPEND_EXPORT + _exportSerializer
            .Serialize(crsDict)
            .UnescapeUnicodeCodePoints();
    }

    public async Task<bool> ImportCrsAsync(ulong? guildId, string input)
    {
        Dictionary<string, List<ExportedTriggers>> data;
        try
        {
            data = Yaml.Deserializer.Deserialize<Dictionary<string, List<ExportedTriggers>>>(input);
            if (data.Sum(x => x.Value.Count) == 0)
                return false;
        }
        catch
        {
            return false;
        }

        await using var uow = _db.GetDbContext();
        foreach (var (trigger, value) in data)
        {
            await uow.ChatTriggers.AddRangeAsync(value
                                                             .Where(ct => !string.IsNullOrWhiteSpace(ct.Res))
                                                             .Select(ct => new Database.Models.ChatTriggers
                                                             {
                                                                 GuildId = guildId,
                                                                 Response = ct.Res,
                                                                 Reactions = ct.React?.JoinWith("@@@"),
                                                                 Trigger = trigger,
                                                                 AllowTarget = ct.At,
                                                                 ContainsAnywhere = ct.Ca,
                                                                 DmResponse = ct.Dm,
                                                                 AutoDeleteTrigger = ct.Ad,
                                                                 NoRespond = ct.Nr
                                                             }));
        }

        await uow.SaveChangesAsync();
        await TriggerReloadChatTriggers();
        return true;
    }

    private async Task ReloadInternal(IReadOnlyList<ulong> allGuildIds)
    {
        await using var uow = _db.GetDbContext();
        var guildItems = await uow.ChatTriggers
            .AsNoTracking()
            .Where(x => allGuildIds.Contains(x.GuildId.Value))
            .ToListAsync();

        newGuildReactions = guildItems
            .GroupBy(k => k.GuildId!.Value)
            .ToDictionary(g => g.Key,
                g => g.Select(x =>
                {
                    x.Trigger = x.Trigger.Replace(MENTION_PH, _client.CurrentUser.Mention);
                    return x;
                }).ToArray())
            .ToConcurrent();

        lock (_gcrWriteLock)
        {
            var globalItems = 
                uow.ChatTriggers
                .AsNoTracking()
                .Where(x => x.GuildId == null || x.GuildId == 0)
                .AsEnumerable()
                .Select(x =>
                {
                    x.Trigger = x.Trigger.Replace(MENTION_PH, _client.CurrentUser.Mention);
                    return x;
                })
                .ToArray();

            globalReactions = globalItems;
        }

        ready = true;
    }

    private Database.Models.ChatTriggers TryGetChatTriggers(IUserMessage umsg)
    {
        if (!ready)
            return null;

        if (umsg.Channel is not SocketTextChannel channel)
            return null;

        var content = umsg.Content.Trim().ToLowerInvariant();

        if (newGuildReactions.TryGetValue(channel.Guild.Id, out var reactions) && reactions.Length > 0)
        {
            var cr = MatchChatTriggerss(content, reactions);
            if (cr is not null)
                return cr;
        }

        var localGrs = globalReactions;

        return MatchChatTriggerss(content, localGrs);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Database.Models.ChatTriggers MatchChatTriggerss(in ReadOnlySpan<char> content, Database.Models.ChatTriggers[] crs)
    {
        var result = new List<Database.Models.ChatTriggers>(1);
        for (var i = 0; i < crs.Length; i++)
        {
            var cr = crs[i];
            var trigger = cr.Trigger;
            if (content.Length > trigger.Length)
            {
                // if input is greater than the trigger, it can only work if:
                // it has CA enabled
                if (cr.ContainsAnywhere)
                {
                    // if ca is enabled, we have to check if it is a word within the content
                    var wp = content.GetWordPosition(trigger);

                    // if it is, then that's valid
                    if (wp != WordPosition.None) result.Add(cr);

                    // if it's not, then it cant' work under any circumstance,
                    // because content is greater than the trigger length
                    // so it can't be equal, and it's not contained as a word
                    continue;
                }

                // if CA is disabled, and CR has AllowTarget, then the
                // content has to start with the trigger followed by a space
                if (cr.AllowTarget && content.StartsWith(trigger, StringComparison.OrdinalIgnoreCase)
                                   && content[trigger.Length] == ' ')
                    result.Add(cr);
            }
            else if (content.Length < cr.Trigger.Length)
            {
                // if input length is less than trigger length, it means
                // that the reaction can never be triggered
            }
            else
            {
                // if input length is the same as trigger length
                // reaction can only trigger if the strings are equal
                if (content.SequenceEqual(cr.Trigger)) result.Add(cr);
            }
        }

        if (result.Count == 0)
            return null;

        return result[_rng.Next(0, result.Count)];
    }

    public async Task ResetCrReactions(ulong? maybeGuildId, int id)
    {
        Database.Models.ChatTriggers cr;
        await using var uow = _db.GetDbContext();
        cr = uow.ChatTriggers.GetById(id);
        if (cr is null)
            return;

        cr.Reactions = string.Empty;

        await uow.SaveChangesAsync();
    }

    private Task UpdateInternalAsync(ulong? maybeGuildId, Database.Models.ChatTriggers cr)
    {
        if (maybeGuildId is { } guildId)
            UpdateInternal(guildId, cr);
        else
            return _pubSub.Pub(_gcrEditedKey, cr);

        return Task.CompletedTask;
    }

    private void UpdateInternal(ulong? maybeGuildId, Database.Models.ChatTriggers cr)
    {
        if (maybeGuildId is ulong guildId)
            newGuildReactions.AddOrUpdate(guildId, new[] {cr},
                (_, old) =>
                {
                    var newArray = old.ToArray();
                    for (var i = 0; i < newArray.Length; i++)
                        if (newArray[i].Id == cr.Id)
                            newArray[i] = cr;
                    return newArray;
                });
        else
            lock (_gcrWriteLock)
            {
                var crs = globalReactions;
                for (var i = 0; i < crs.Length; i++)
                    if (crs[i].Id == cr.Id)
                        crs[i] = cr;
            }
    }

    private Task AddInternalAsync(ulong? maybeGuildId, Database.Models.ChatTriggers cr)
    {
        // only do this for perf purposes
        cr.Trigger = cr.Trigger.Replace(MENTION_PH, _client.CurrentUser.Mention);

        if (maybeGuildId is ulong guildId)
            newGuildReactions.AddOrUpdate(guildId,
                new[] {cr},
                (_, old) => old.With(cr));
        else
            return _pubSub.Pub(_gcrAddedKey, cr);

        return Task.CompletedTask;
    }

    private Task DeleteInternalAsync(ulong? maybeGuildId, int id)
    {
        if (maybeGuildId is ulong guildId)
        {
            newGuildReactions.AddOrUpdate(guildId,
                Array.Empty<Database.Models.ChatTriggers>(),
                (key, old) => DeleteInternal(old, id, out _));

            return Task.CompletedTask;
        }

        lock (_gcrWriteLock)
        {
            var cr = Array.Find(globalReactions, item => item.Id == id);
            if (cr is not null) return _pubSub.Pub(_gcrDeletedkey, cr.Id);
        }

        return Task.CompletedTask;
    }

    private static Database.Models.ChatTriggers[] DeleteInternal(IReadOnlyList<Database.Models.ChatTriggers>? crs, int id, out Database.Models.ChatTriggers deleted)
    {
        deleted = null;
        if (crs is null || crs.Count == 0)
            return crs as Database.Models.ChatTriggers[] ?? crs?.ToArray();

        var newCrs = new Database.Models.ChatTriggers[crs.Count - 1];
        for (int i = 0, k = 0; i < crs.Count; i++, k++)
        {
            if (crs[i].Id == id)
            {
                deleted = crs[i];
                k--;
                continue;
            }

            newCrs[k] = crs[i];
        }

        return newCrs;
    }

    public async Task SetCrReactions(ulong? guildId, int id, IEnumerable<string> emojis)
    {
        Database.Models.ChatTriggers cr;
        await using (var uow = _db.GetDbContext())
        {
            cr = uow.ChatTriggers.GetById(id);
            if (cr is null)
                return;

            cr.Reactions = string.Join("@@@", emojis);

            await uow.SaveChangesAsync();
        }

        await UpdateInternalAsync(guildId, cr);
    }

    public async Task<(bool Sucess, bool NewValue)> ToggleCrOptionAsync(int id, CtField field)
    {
        var newVal = false;
        Database.Models.ChatTriggers ct;
        await using (var uow = _db.GetDbContext())
        {
            ct = uow.ChatTriggers.GetById(id);
            if (ct is null)
                return (false, false);
            newVal = field switch
            {
                CtField.AutoDelete => ct.AutoDeleteTrigger = !ct.AutoDeleteTrigger,
                CtField.ContainsAnywhere => ct.ContainsAnywhere = !ct.ContainsAnywhere,
                CtField.DmResponse => ct.DmResponse = !ct.DmResponse,
                CtField.AllowTarget => ct.AllowTarget = !ct.AllowTarget,
                CtField.ReactToTrigger => ct.ReactToTrigger = !ct.ReactToTrigger,
                CtField.NoRespond => ct.NoRespond = !ct.NoRespond,
                _ => newVal
            };

            await uow.SaveChangesAsync();
        }

        await UpdateInternalAsync(ct.GuildId, ct);

        return (true, newVal);
    }

    public Database.Models.ChatTriggers GetChatTriggers(ulong? guildId, int id)
    {
        using var uow = _db.GetDbContext();
        var cr = uow.ChatTriggers.GetById(id);
        if (cr == null || cr.GuildId != guildId)
            return null;

        return cr;
    }

    public int DeleteAllChatTriggers(ulong guildId)
    {
        using var uow = _db.GetDbContext();
        var count = uow.ChatTriggers.ClearFromGuild(guildId);
        uow.SaveChanges();

        newGuildReactions.TryRemove(guildId, out _);

        return count;
    }

    public bool ReactionExists(ulong? guildId, string input)
    {
        using var uow = _db.GetDbContext();
        var cr = uow.ChatTriggers.GetByGuildIdAndInput(guildId, input);
        return cr != null;
    }

    public Task OnReadyAsync() => ReloadInternal(_bot.GetCurrentGuildIds());

    private ValueTask OnCrsShouldReload(bool _) => new(ReloadInternal(_bot.GetCurrentGuildIds()));

    private ValueTask OnGcrAdded(Database.Models.ChatTriggers c)
    {
        lock (_gcrWriteLock)
        {
            var newGlobalReactions = new Database.Models.ChatTriggers[globalReactions.Length + 1];
            Array.Copy(globalReactions, newGlobalReactions, globalReactions.Length);
            newGlobalReactions[globalReactions.Length] = c;
            globalReactions = newGlobalReactions;
        }

        return default;
    }

    private ValueTask OnGcrEdited(Database.Models.ChatTriggers c)
    {
        lock (_gcrWriteLock)
        {
            for (var i = 0; i < globalReactions.Length; i++)
                if (globalReactions[i].Id == c.Id)
                {
                    globalReactions[i] = c;
                    return default;
                }

            // if edited cr is not found?!
            // add it
            OnGcrAdded(c);
        }

        return default;
    }

    private ValueTask OnGcrDeleted(int id)
    {
        lock (_gcrWriteLock)
        {
            var newGlobalReactions = DeleteInternal(globalReactions, id, out _);
            globalReactions = newGlobalReactions;
        }

        return default;
    }

    public Task TriggerReloadChatTriggers() => _pubSub.Pub(_crsReloadedKey, true);

    private Task OnLeftGuild(SocketGuild arg)
    {
        newGuildReactions.TryRemove(arg.Id, out _);

        return Task.CompletedTask;
    }

    private async Task OnJoinedGuild(GuildConfig gc)
    {
        await using var uow = _db.GetDbContext();
        var crs = await 
            uow
            .ChatTriggers
            .AsNoTracking()
            .Where(x => x.GuildId == gc.GuildId)
            .ToArrayAsync();

        newGuildReactions[gc.GuildId] = crs;
    }
    

    public async Task<Database.Models.ChatTriggers> AddAsync(ulong? guildId, string key, string message)
    {
        key = key.ToLowerInvariant();
        var cr = new Database.Models.ChatTriggers
        {
            GuildId = guildId,
            Trigger = key,
            Response = message
        };

        if (cr.Response.Contains("%target%", StringComparison.OrdinalIgnoreCase))
            cr.AllowTarget = true;

        await using (var uow = _db.GetDbContext())
        {
            uow.ChatTriggers.Add(cr);
            await uow.SaveChangesAsync();
        }

        await AddInternalAsync(guildId, cr);

        return cr;
    }

    public async Task<Database.Models.ChatTriggers> EditAsync(ulong? guildId, int id, string message)
    {
        await using var uow = _db.GetDbContext();
        var cr = uow.ChatTriggers.GetById(id);

        if (cr == null || cr.GuildId != guildId)
            return null;

        // disable allowtarget if message had target, but it was removed from it
        if (!message.Contains("%target%", StringComparison.OrdinalIgnoreCase)
            && cr.Response.Contains("%target%", StringComparison.OrdinalIgnoreCase))
            cr.AllowTarget = false;

        cr.Response = message;

        // enable allow target if message is edited to contain target
        if (cr.Response.Contains("%target%", StringComparison.OrdinalIgnoreCase))
            cr.AllowTarget = true;

        await uow.SaveChangesAsync();
        await UpdateInternalAsync(guildId, cr);

        return cr;
    }


    public async Task<Database.Models.ChatTriggers> DeleteAsync(ulong? guildId, int id)
    {
        await using var uow = _db.GetDbContext();
        var toDelete = uow.ChatTriggers.GetById(id);

        if (toDelete is null)
            return null;

        if ((toDelete.IsGlobal() && guildId == null) || guildId == toDelete.GuildId)
        {
            uow.ChatTriggers.Remove(toDelete);
            await uow.SaveChangesAsync();
            await DeleteInternalAsync(guildId, id);
            return toDelete;
        }

        return null;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Database.Models.ChatTriggers[] GetChatTriggersFor(ulong? maybeGuildId)
    {
        if (maybeGuildId is { } guildId)
            return newGuildReactions.TryGetValue(guildId, out var crs)
                ? crs
                : Array.Empty<Database.Models.ChatTriggers>();
        return Array.Empty<Database.Models.ChatTriggers>();
    }
}