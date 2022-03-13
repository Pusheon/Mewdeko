using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using Discord;
using Discord.WebSocket;
using Humanizer;
using Mewdeko._Extensions;
using Mewdeko.Common;
using Mewdeko.Common.Collections;
using Mewdeko.Database;
using Mewdeko.Database.Extensions;
using Mewdeko.Database.Models;
using Mewdeko.Modules.Xp.Common;
using Mewdeko.Services.Impl;
using Mewdeko.Services.strings;
using Newtonsoft.Json;
using Serilog;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using StackExchange.Redis;
using Color = SixLabors.ImageSharp.Color;
using Image = SixLabors.ImageSharp.Image;

namespace Mewdeko.Modules.Xp.Services;

public class XpService : INService, IUnloadableService
{
    public const int XP_REQUIRED_LVL_1 = 36;

    private readonly ConcurrentQueue<UserCacheItem> _addMessageXp = new();

    private readonly IDataCache _cache;
    private readonly DiscordSocketClient _client;
    private readonly CommandHandler _cmd;
    private readonly IBotCredentials _creds;
    private readonly ICurrencyService _cs;

    private readonly DbService _db;

    private readonly ConcurrentDictionary<ulong, ConcurrentHashSet<ulong>> _excludedChannels;

    private readonly ConcurrentDictionary<ulong, ConcurrentHashSet<ulong>> _excludedRoles;

    private readonly ConcurrentHashSet<ulong> _excludedServers;
    private readonly FontProvider _fonts;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IImageCache _images;
    private readonly IBotStrings _strings;
    private readonly XpConfigService _xpConfig;
    private XpTemplate template;

    public XpService(
        DiscordSocketClient client,
        CommandHandler cmd,
        Mewdeko bot,
        DbService db,
        IBotStrings strings,
        IDataCache cache,
        FontProvider fonts,
        IBotCredentials creds,
        ICurrencyService cs,
        IHttpClientFactory http,
        XpConfigService xpConfig)
    {
        _db = db;
        _cmd = cmd;
        _images = cache.LocalImages;
        _strings = strings;
        _cache = cache;
        _fonts = fonts;
        _creds = creds;
        _cs = cs;
        _httpFactory = http;
        _xpConfig = xpConfig;
        _excludedServers = new ConcurrentHashSet<ulong>();
        _excludedChannels = new ConcurrentDictionary<ulong, ConcurrentHashSet<ulong>>();
        _client = client;

        InternalReloadXpTemplate();

        if (client.ShardId == 0)
        {
            var sub = _cache.Redis.GetSubscriber();
            sub.Subscribe($"{_creds.RedisKey()}_reload_xp_template", (_, _) => InternalReloadXpTemplate());
        }

        //load settings
        var allGuildConfigs = bot.AllGuildConfigs.Where(x => x.XpSettings != null).ToList();
        XpTxtRates = bot.AllGuildConfigs.ToDictionary(x => x.GuildId, x => x.XpTxtRate).ToConcurrent();
        XpTxtTimeouts = bot.AllGuildConfigs.ToDictionary(x => x.GuildId, x => x.XpTxtTimeout).ToConcurrent();
        XpVoiceRates = bot.AllGuildConfigs.ToDictionary(x => x.GuildId, x => x.XpVoiceRate).ToConcurrent();
        XpVoiceTimeouts = bot.AllGuildConfigs.ToDictionary(x => x.GuildId, x => x.XpVoiceTimeout).ToConcurrent();
        _excludedChannels = allGuildConfigs.ToDictionary(x => x.GuildId,
            x => new ConcurrentHashSet<ulong>(x.XpSettings.ExclusionList
                                               .Where(ex => ex.ItemType == ExcludedItemType.Channel)
                                               .Select(ex => ex.ItemId).Distinct())).ToConcurrent();

        _excludedRoles = allGuildConfigs.ToDictionary(x => x.GuildId,
            x => new ConcurrentHashSet<ulong>(x.XpSettings.ExclusionList
                                               .Where(ex => ex.ItemType == ExcludedItemType.Role)
                                               .Select(ex => ex.ItemId).Distinct())).ToConcurrent();

        _excludedServers = new ConcurrentHashSet<ulong>(
            allGuildConfigs.Where(x => x.XpSettings.ServerExcluded).Select(x => x.GuildId));

        _cmd.OnMessageNoTrigger += Cmd_OnMessageNoTrigger;

#if !GLOBAL_Mewdeko
        _client.UserVoiceStateUpdated += Client_OnUserVoiceStateUpdated;

        // Scan guilds on startup.
        _client.GuildAvailable += Client_OnGuildAvailable;
        foreach (var guild in _client.Guilds) Client_OnGuildAvailable(guild);
#endif
        Task.Run(UpdateLoop);
    }

    private ConcurrentDictionary<ulong, int> XpTxtRates { get; } = new();
    private ConcurrentDictionary<ulong, int> XpVoiceRates { get; } = new();
    private ConcurrentDictionary<ulong, int> XpTxtTimeouts { get; } = new();
    private ConcurrentDictionary<ulong, int> XpVoiceTimeouts { get; } = new();

    public Task Unload()
    {
        _cmd.OnMessageNoTrigger -= Cmd_OnMessageNoTrigger;
        _client.UserVoiceStateUpdated -= Client_OnUserVoiceStateUpdated;
        _client.GuildAvailable -= Client_OnGuildAvailable;
        return Task.CompletedTask;
    }

    private async Task UpdateLoop()
    {
        while (true)
        {
            await Task.Delay(TimeSpan.FromSeconds(5));
            try
            {
                var toNotify =
                    new List<(IGuild Guild, IMessageChannel MessageChannel, IUser User, int Level,
                        XpNotificationLocation NotifyType, NotifOf NotifOf)>();
                var roleRewards = new Dictionary<ulong, List<XpRoleReward>>();
                var curRewards = new Dictionary<ulong, List<XpCurrencyReward>>();

                var toAddTo = new List<UserCacheItem>();
                while (_addMessageXp.TryDequeue(out var usr)) toAddTo.Add(usr);

                var group = toAddTo.GroupBy(x => (GuildId: x.Guild.Id, x.User));
                if (toAddTo.Count == 0) continue;

                await using (var uow = _db.GetDbContext())
                {
                    foreach (var item in group)
                    {
                        var xp = item.Sum(x => x.XpAmount);

                        //1. Mass query discord users and userxpstats and get them from local dict
                        //2. (better but much harder) Move everything to the database, and get old and new xp
                        // amounts for every user (in order to give rewards)

                        var usr = uow.UserXpStats.GetOrCreateUser(item.Key.GuildId, item.Key.User.Id);
                        var du = uow.GetOrCreateUser(item.Key.User);

                        var globalXp = du.TotalXp;
                        var oldGlobalLevelData = new LevelStats(globalXp);
                        var newGlobalLevelData = new LevelStats(globalXp + xp);

                        var oldGuildLevelData = new LevelStats(usr.Xp + usr.AwardedXp);
                        usr.Xp += xp;
                        du.TotalXp += xp;
                        if (du.Club != null) du.Club.Xp += xp;
                        var newGuildLevelData = new LevelStats(usr.Xp + usr.AwardedXp);

                        if (oldGlobalLevelData.Level < newGlobalLevelData.Level)
                        {
                            du.LastLevelUp = DateTime.UtcNow;
                            var first = item.First();
                            if (du.NotifyOnLevelUp != XpNotificationLocation.None)
                                toNotify.Add((first.Guild, first.Channel, first.User, newGlobalLevelData.Level,
                                    du.NotifyOnLevelUp, NotifOf.Global));
                        }

                        if (oldGuildLevelData.Level >= newGuildLevelData.Level) continue;
                        {
                            usr.LastLevelUp = DateTime.UtcNow;
                            //send level up notification
                            var first = item.First();
                            if (usr.NotifyOnLevelUp != XpNotificationLocation.None)
                                toNotify.Add((first.Guild, first.Channel, first.User, newGuildLevelData.Level,
                                    usr.NotifyOnLevelUp, NotifOf.Server));

                            //give role
                            if (!roleRewards.TryGetValue(usr.GuildId, out var rrews))
                            {
                                rrews = uow.XpSettingsFor(usr.GuildId).RoleRewards.ToList();
                                roleRewards.Add(usr.GuildId, rrews);
                            }

                            if (!curRewards.TryGetValue(usr.GuildId, out var crews))
                            {
                                crews = uow.XpSettingsFor(usr.GuildId).CurrencyRewards.ToList();
                                curRewards.Add(usr.GuildId, crews);
                            }

                            for (var i = oldGuildLevelData.Level + 1; i <= newGuildLevelData.Level; i++)
                            {
                                var rrew = rrews.FirstOrDefault(x => x.Level == i);
                                if (rrew != null)
                                {
                                    var role = first.User.Guild.GetRole(rrew.RoleId);
                                    if (role is not null) _ = first.User.AddRoleAsync(role);
                                }

                                var crew = crews.FirstOrDefault(x => x.Level == i);
                                if (crew != null)
                                    //give the user the reward if it exists
                                    await _cs.AddAsync(item.Key.User.Id, "Level-up Reward", crew.Amount);
                            }
                        }
                    }

                    await uow.SaveChangesAsync();
                }

                await Task.WhenAll(toNotify.Select(async x =>
                {
                    if (x.NotifOf == NotifOf.Server)
                    {
                        if (x.NotifyType == XpNotificationLocation.Dm)
                        {
                            var chan = await x.User.CreateDMChannelAsync();
                            if (chan != null)
                                await chan.SendConfirmAsync(_strings.GetText("level_up_dm", x.Guild.Id, x.User.Mention,
                                    Format.Bold(x.Level.ToString()), Format.Bold(x.Guild.ToString() ?? "-")));
                        }
                        else if (x.MessageChannel != null) // channel
                        {
                            await x.MessageChannel.SendConfirmAsync(_strings.GetText("level_up_channel", x.Guild.Id,
                                x.User.Mention, Format.Bold(x.Level.ToString())));
                        }
                    }
                    else
                    {
                        IMessageChannel chan;
                        if (x.NotifyType == XpNotificationLocation.Dm)
                            chan = await x.User.CreateDMChannelAsync();
                        else // channel
                            chan = x.MessageChannel;

                        await chan.SendConfirmAsync(_strings.GetText("level_up_global", x.Guild.Id, x.User.Mention,
                            Format.Bold(x.Level.ToString())));
                    }
                }));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error In the XP update loop");
            }
        }
    }


    private void InternalReloadXpTemplate()
    {
        try
        {
            var settings = new JsonSerializerSettings
            {
                ContractResolver = new RequireObjectPropertiesContractResolver()
            };
            template = JsonConvert.DeserializeObject<XpTemplate>(File.ReadAllText("./data/xp_template.json"), settings);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Xp template is invalid. Loaded default values");
            template = new XpTemplate();
            File.WriteAllText("./data/xp_template_backup.json",
                JsonConvert.SerializeObject(template, Formatting.Indented));
        }
    }

    public void ReloadXpTemplate()
    {
        var sub = _cache.Redis.GetSubscriber();
        sub.Publish($"{_creds.RedisKey()}_reload_xp_template", "");
    }

    public void SetCurrencyReward(ulong guildId, int level, int amount)
    {
        using var uow = _db.GetDbContext();
        var settings = uow.XpSettingsFor(guildId);

        if (amount <= 0)
        {
            var toRemove = settings.CurrencyRewards.FirstOrDefault(x => x.Level == level);
            if (toRemove != null)
            {
                uow.Remove(toRemove);
                settings.CurrencyRewards.Remove(toRemove);
            }
        }
        else
        {
            var rew = settings.CurrencyRewards.FirstOrDefault(x => x.Level == level);

            if (rew != null)
                rew.Amount = amount;
            else
                settings.CurrencyRewards.Add(new XpCurrencyReward { Level = level, Amount = amount });
        }

        uow.SaveChanges();
    }

    public IEnumerable<XpCurrencyReward> GetCurrencyRewards(ulong id)
    {
        using var uow = _db.GetDbContext();
        return uow.XpSettingsFor(id).CurrencyRewards.ToArray();
    }

    public IEnumerable<XpRoleReward> GetRoleRewards(ulong id)
    {
        using var uow = _db.GetDbContext();
        return uow.XpSettingsFor(id).RoleRewards.ToArray();
    }

    public void SetRoleReward(ulong guildId, int level, ulong? roleId)
    {
        using var uow = _db.GetDbContext();
        var settings = uow.XpSettingsFor(guildId);

        if (roleId == null)
        {
            var toRemove = settings.RoleRewards.FirstOrDefault(x => x.Level == level);
            if (toRemove != null)
            {
                uow.Remove(toRemove);
                settings.RoleRewards.Remove(toRemove);
            }
        }
        else
        {
            var rew = settings.RoleRewards.FirstOrDefault(x => x.Level == level);

            if (rew != null)
                rew.RoleId = roleId.Value;
            else
                settings.RoleRewards.Add(new XpRoleReward { Level = level, RoleId = roleId.Value });
        }

        uow.SaveChanges();
    }

    public List<UserXpStats> GetUserXps(ulong guildId, int page)
    {
        using var uow = _db.GetDbContext();
        return uow.UserXpStats.GetUsersFor(guildId, page);
    }

    public List<UserXpStats> GetTopUserXps(ulong guildId, int count)
    {
        using var uow = _db.GetDbContext();
        return uow.UserXpStats.GetTopUserXps(guildId, count);
    }

    public DiscordUser[] GetUserXps(int page)
    {
        using var uow = _db.GetDbContext();
        return uow.DiscordUser.GetUsersXpLeaderboardFor(page);
    }

    public async Task ChangeNotificationType(ulong userId, ulong guildId, XpNotificationLocation type)
    {
        await using var uow = _db.GetDbContext();
        var user = uow.UserXpStats.GetOrCreateUser(guildId, userId);
        user.NotifyOnLevelUp = type;
        await uow.SaveChangesAsync();
    }

    public XpNotificationLocation GetNotificationType(ulong userId, ulong guildId)
    {
        using var uow = _db.GetDbContext();
        var user = uow.UserXpStats.GetOrCreateUser(guildId, userId);
        return user.NotifyOnLevelUp;
    }

    public XpNotificationLocation GetNotificationType(IUser user)
    {
        using var uow = _db.GetDbContext();
        return uow.GetOrCreateUser(user).NotifyOnLevelUp;
    }

    public async Task ChangeNotificationType(IUser user, XpNotificationLocation type)
    {
        await using var uow = _db.GetDbContext();
        var du = uow.GetOrCreateUser(user);
        du.NotifyOnLevelUp = type;
        await uow.SaveChangesAsync();
    }

    private Task Client_OnGuildAvailable(SocketGuild guild)
    {
        Task.Run(() =>
        {
            foreach (var channel in guild.VoiceChannels) ScanChannelForVoiceXp(channel);
        });

        return Task.CompletedTask;
    }

    private Task Client_OnUserVoiceStateUpdated(SocketUser socketUser, SocketVoiceState before, SocketVoiceState after)
    {
        if (socketUser is not SocketGuildUser user || user.IsBot)
            return Task.CompletedTask;

        var _ = Task.Run(() =>
        {
            if (before.VoiceChannel != null) ScanChannelForVoiceXp(before.VoiceChannel);

            if (after.VoiceChannel != null && after.VoiceChannel != before.VoiceChannel)
                ScanChannelForVoiceXp(after.VoiceChannel);
            else if (after.VoiceChannel == null)
                // In this case, the user left the channel and the previous for loops didn't catch
                // it because it wasn't in any new channel. So we need to get rid of it.
                UserLeftVoiceChannel(user, before.VoiceChannel);
        });

        return Task.CompletedTask;
    }

    private void ScanChannelForVoiceXp(SocketVoiceChannel channel)
    {
        if (ShouldTrackVoiceChannel(channel))
            foreach (var user in channel.Users)
                ScanUserForVoiceXp(user, channel);
        else
            foreach (var user in channel.Users)
                UserLeftVoiceChannel(user, channel);
    }

    /// <summary>
    ///     Assumes that the channel itself is valid and adding xp.
    /// </summary>
    /// <param name="user"></param>
    /// <param name="channel"></param>
    private void ScanUserForVoiceXp(SocketGuildUser user, SocketVoiceChannel channel)
    {
        if (UserParticipatingInVoiceChannel(user) && ShouldTrackXp(user, channel.Id))
            UserJoinedVoiceChannel(user);
        else
            UserLeftVoiceChannel(user, channel);
    }

    private static bool ShouldTrackVoiceChannel(SocketVoiceChannel channel) =>
        channel.Users.Where(UserParticipatingInVoiceChannel).Take(2).Count() >= 2;

    private static bool UserParticipatingInVoiceChannel(SocketGuildUser user) =>
        !user.IsDeafened && !user.IsMuted && !user.IsSelfDeafened && !user.IsSelfMuted;

    private void UserJoinedVoiceChannel(SocketGuildUser user)
    {
        var key = $"{_creds.RedisKey()}_user_xp_vc_join_{user.Id}";
        var value = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        int e;
        e = GetVoiceXpTimeout(user.Guild.Id) == 0 ? _xpConfig.Data.VoiceMaxMinutes : GetVoiceXpTimeout(user.Guild.Id);
        _cache.Redis.GetDatabase().StringSet(key, value, TimeSpan.FromMinutes(e), When.NotExists);
    }

    public int GetXpTimeout(ulong? id)
    {
        XpTxtTimeouts.TryGetValue(id.Value, out var snum);
        return snum;
    }

    public int GetTxtXpRate(ulong? id)
    {
        XpTxtRates.TryGetValue(id.Value, out var snum);
        return snum;
    }

    public double GetVoiceXpRate(ulong? id)
    {
        XpVoiceRates.TryGetValue(id.Value, out var snum);
        return snum;
    }

    public int GetVoiceXpTimeout(ulong? id)
    {
        XpVoiceTimeouts.TryGetValue(id.Value, out var snum);
        return snum;
    }

    private void UserLeftVoiceChannel(SocketGuildUser user, SocketVoiceChannel channel)
    {
        var key = $"{_creds.RedisKey()}_user_xp_vc_join_{user.Id}";
        var value = _cache.Redis.GetDatabase().StringGet(key);
        _cache.Redis.GetDatabase().KeyDelete(key);

        // Allow for if this function gets called multiple times when a user leaves a channel.
        if (value.IsNull) return;

        if (!value.TryParse(out long startUnixTime))
            return;

        var dateStart = DateTimeOffset.FromUnixTimeSeconds(startUnixTime);
        var dateEnd = DateTimeOffset.UtcNow;
        var minutes = (dateEnd - dateStart).TotalMinutes;
        double ten;
        ten = GetVoiceXpRate(user.Guild.Id) == 0 ? _xpConfig.Data.VoiceXpPerMinute : GetVoiceXpRate(user.Guild.Id);
        var xp = ten * minutes;
        var actualXp = (int)Math.Floor(xp);

        if (actualXp > 0)
            _addMessageXp.Enqueue(new UserCacheItem { Guild = channel.Guild, User = user, XpAmount = actualXp });
    }

    private bool ShouldTrackXp(SocketGuildUser user, ulong channelId)
    {
        if (_excludedChannels.TryGetValue(user.Guild.Id, out var chans) && chans.Contains(channelId)) return false;

        if (_excludedServers.Contains(user.Guild.Id)) return false;

        return !_excludedRoles.TryGetValue(user.Guild.Id, out var roles) || !user.Roles.Any(x => roles.Contains(x.Id));
    }

    private Task Cmd_OnMessageNoTrigger(IUserMessage arg)
    {
        if (arg.Author is not SocketGuildUser user || user.IsBot)
            return Task.CompletedTask;

        var _ = Task.Run(() =>
        {
            if (!ShouldTrackXp(user, arg.Channel.Id))
                return;

            if (!arg.Content.Contains(' ') && arg.Content.Length < 5)
                return;

            if (!SetUserRewarded(user))
                return;
            int e;
            e = GetTxtXpRate(user.Guild.Id) == 0 ? _xpConfig.Data.XpPerMessage : GetTxtXpRate(user.Guild.Id);
            _addMessageXp.Enqueue(new UserCacheItem
            {
                Guild = user.Guild, Channel = arg.Channel, User = user, XpAmount = e
            });
        });
        return Task.CompletedTask;
    }

    public void AddXpDirectly(IGuildUser user, IMessageChannel channel, int amount)
    {
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));

        _addMessageXp.Enqueue(new UserCacheItem
        {
            Guild = user.Guild, Channel = channel, User = user, XpAmount = amount
        });
    }

    public void AddXp(ulong userId, ulong guildId, int amount)
    {
        using var uow = _db.GetDbContext();
        var usr = uow.UserXpStats.GetOrCreateUser(guildId, userId);

        usr.AwardedXp += amount;

        uow.SaveChanges();
    }

    public async Task XpTxtRateSet(IGuild guild, int num)
    {
        await using var uow = _db.GetDbContext();
        var gc = uow.ForGuildId(guild.Id, set => set);
        gc.XpTxtRate = num;
        await uow.SaveChangesAsync();

        XpTxtRates.AddOrUpdate(guild.Id, num, (_, _) => num);
    }

    public async Task XpTxtTimeoutSet(IGuild guild, int num)
    {
        await using var uow = _db.GetDbContext();
        var gc = uow.ForGuildId(guild.Id, set => set);
        gc.XpTxtTimeout = num;
        await uow.SaveChangesAsync();

        XpTxtTimeouts.AddOrUpdate(guild.Id, num, (_, _) => num);
    }

    public async Task XpVoiceRateSet(IGuild guild, int num)
    {
        await using var uow = _db.GetDbContext();
        var gc = uow.ForGuildId(guild.Id, set => set);
        gc.XpVoiceRate = num;
        await uow.SaveChangesAsync();

        XpVoiceRates.AddOrUpdate(guild.Id, num, (_, _) => num);
    }

    public async Task XpVoiceTimeoutSet(IGuild guild, int num)
    {
        await using var uow = _db.GetDbContext();
        var gc = uow.ForGuildId(guild.Id, set => set);
        gc.XpVoiceTimeout = num;
        await uow.SaveChangesAsync();

        XpVoiceTimeouts.AddOrUpdate(guild.Id, num, (_, _) => num);
    }

    public bool IsServerExcluded(ulong id) => _excludedServers.Contains(id);

    public IEnumerable<ulong> GetExcludedRoles(ulong id) =>
        _excludedRoles.TryGetValue(id, out var val) ? val.ToArray() : Enumerable.Empty<ulong>();

    public IEnumerable<ulong> GetExcludedChannels(ulong id) => _excludedChannels.TryGetValue(id, out var val)
        ? val.ToArray()
        : Enumerable.Empty<ulong>();

    private bool SetUserRewarded(SocketGuildUser userId)
    {
        var r = _cache.Redis.GetDatabase();
        var key = $"{_creds.RedisKey()}_user_xp_gain_{userId.Id}";
        int e;
        e = GetXpTimeout(userId.Guild.Id) == 0 ? _xpConfig.Data.MessageXpCooldown : GetXpTimeout(userId.Guild.Id);
        return r.StringSet(key, true, TimeSpan.FromMinutes(e), When.NotExists);
    }

    public async Task<FullUserStats> GetUserStatsAsync(IGuildUser user)
    {
        DiscordUser du;
        UserXpStats stats = null;
        int totalXp;
        int globalRank;
        int guildRank;
        await using (var uow = _db.GetDbContext())
        {
            du = uow.GetOrCreateUser(user);
            totalXp = du.TotalXp;
            globalRank = uow.DiscordUser.GetUserGlobalRank(user.Id);
            guildRank = uow.UserXpStats.GetUserGuildRanking(user.Id, user.GuildId);
            stats = uow.UserXpStats.GetOrCreateUser(user.GuildId, user.Id);
            await uow.SaveChangesAsync();
        }

        return new FullUserStats(du, stats, new LevelStats(totalXp), new LevelStats(stats.Xp + stats.AwardedXp),
            globalRank, guildRank);
    }

    public (int Level, int LevelXp, int LevelRequiredXp) GetLevelData(UserXpStats stats)
    {
        const int baseXp = XP_REQUIRED_LVL_1;
        int required;
        var totalXp = 0;
        var lvl = 1;
        while (true)
        {
            required = (int)(baseXp + (baseXp / 4.0 * (lvl - 1)));

            if (required + totalXp > stats.Xp)
                break;

            totalXp += required;
            lvl++;
        }

        return (lvl - 1, stats.Xp - totalXp, required);
    }

    public bool ToggleExcludeServer(ulong id)
    {
        using var uow = _db.GetDbContext();
        var xpSetting = uow.XpSettingsFor(id);
        if (_excludedServers.Add(id))
        {
            xpSetting.ServerExcluded = true;
            uow.SaveChanges();
            return true;
        }

        _excludedServers.TryRemove(id);
        xpSetting.ServerExcluded = false;
        uow.SaveChanges();
        return false;
    }

    public bool ToggleExcludeRole(ulong guildId, ulong rId)
    {
        var roles = _excludedRoles.GetOrAdd(guildId, _ => new ConcurrentHashSet<ulong>());
        using var uow = _db.GetDbContext();
        var xpSetting = uow.XpSettingsFor(guildId);
        var excludeObj = new ExcludedItem { ItemId = rId, ItemType = ExcludedItemType.Role };

        if (roles.Add(rId))
        {
            if (xpSetting.ExclusionList.Add(excludeObj)) uow.SaveChanges();

            return true;
        }

        roles.TryRemove(rId);

        var toDelete = xpSetting.ExclusionList.FirstOrDefault(x => x.Equals(excludeObj));
        if (toDelete == null) return false;
        uow.Remove(toDelete);
        uow.SaveChanges();

        return false;
    }

    public bool ToggleExcludeChannel(ulong guildId, ulong chId)
    {
        var channels = _excludedChannels.GetOrAdd(guildId, _ => new ConcurrentHashSet<ulong>());
        using var uow = _db.GetDbContext();
        var xpSetting = uow.XpSettingsFor(guildId);
        var excludeObj = new ExcludedItem { ItemId = chId, ItemType = ExcludedItemType.Channel };

        if (channels.Add(chId))
        {
            if (xpSetting.ExclusionList.Add(excludeObj)) uow.SaveChanges();

            return true;
        }

        channels.TryRemove(chId);

        if (xpSetting.ExclusionList.Remove(excludeObj)) uow.SaveChanges();

        return false;
    }

    public async Task<(Stream Image, IImageFormat Format)> GenerateXpImageAsync(IGuildUser user)
    {
        var stats = await GetUserStatsAsync(user);
        return await GenerateXpImageAsync(stats);
    }


    private async Task<(Stream Image, IImageFormat Format)> GenerateXpImageAsync(FullUserStats stats)
    {
        var usernameTextOptions = new TextGraphicsOptions
        {
            TextOptions = new TextOptions
            {
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center
            }
        }.WithFallbackFonts(_fonts.FallBackFonts);

        new TextGraphicsOptions
        {
            TextOptions = new TextOptions
            {
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center
            }
        }.WithFallbackFonts(_fonts.FallBackFonts);

        using var img = Image.Load<Rgba32>(_images.XpBackground, out var imageFormat);
        if (template.User.Name.Show)
        {
            var fontSize = (int)(template.User.Name.FontSize * 0.9);
            var username = stats.User.Username;
            var usernameFont = _fonts.NotoSans.CreateFont(fontSize, FontStyle.Bold);

            var size = TextMeasurer.Measure($"{username}", new RendererOptions(usernameFont));
            var scale = 400f / size.Width;
            if (scale < 1)
                usernameFont = _fonts.NotoSans.CreateFont(template.User.Name.FontSize * scale, FontStyle.Bold);

            img.Mutate(x => x.DrawText(usernameTextOptions, username, usernameFont, template.User.Name.Color,
                new PointF(template.User.Name.Pos.X, template.User.Name.Pos.Y + 8)));
        }

        //club name


        if (template.User.GuildLevel.Show)
            img.Mutate(x => x.DrawText(stats.Guild.Level.ToString(),
                _fonts.NotoSans.CreateFont(template.User.GuildLevel.FontSize, FontStyle.Bold),
                template.User.GuildLevel.Color,
                new PointF(template.User.GuildLevel.Pos.X, template.User.GuildLevel.Pos.Y)));


        var pen = new Pen(Color.Black, 1);

        var guild = stats.Guild;

        //xp bar
        if (template.User.Xp.Bar.Show)
        {
            var xpPercent = guild.LevelXp / (float)guild.RequiredXp;
            DrawXpBar(xpPercent, template.User.Xp.Bar.Guild, img);
        }

        if (template.User.Xp.Guild.Show)
            img.Mutate(x => x.DrawText($"{guild.LevelXp}/{guild.RequiredXp}",
                _fonts.UniSans.CreateFont(template.User.Xp.Guild.FontSize, FontStyle.Bold),
                Brushes.Solid(template.User.Xp.Guild.Color),
                new PointF(template.User.Xp.Guild.Pos.X, template.User.Xp.Guild.Pos.Y)));

        if (stats.FullGuildStats.AwardedXp != 0 && template.User.Xp.Awarded.Show)
        {
            var sign = stats.FullGuildStats.AwardedXp > 0 ? "+ " : "";
            var awX = template.User.Xp.Awarded.Pos.X
                      - (Math.Max(0, stats.FullGuildStats.AwardedXp.ToString().Length - 2) * 5);
            var awY = template.User.Xp.Awarded.Pos.Y;
            img.Mutate(x => x.DrawText($"({sign}{stats.FullGuildStats.AwardedXp})",
                _fonts.NotoSans.CreateFont(template.User.Xp.Awarded.FontSize, FontStyle.Bold),
                Brushes.Solid(template.User.Xp.Awarded.Color), pen, new PointF(awX, awY)));
        }

        //ranking

        if (template.User.GuildRank.Show)
            img.Mutate(x => x.DrawText(stats.GuildRanking.ToString(),
                _fonts.UniSans.CreateFont(template.User.GuildRank.FontSize, FontStyle.Bold),
                template.User.GuildRank.Color,
                new PointF(template.User.GuildRank.Pos.X, template.User.GuildRank.Pos.Y)));

        //time on this level

        string GetTimeSpent(DateTime time, string format)
        {
            var offset = DateTime.UtcNow - time;
            return $"{offset.Humanize()} ago";
        }


        if (template.User.TimeOnLevel.Guild.Show)
            img.Mutate(x => x.DrawText(GetTimeSpent(stats.FullGuildStats.LastLevelUp, template.User.TimeOnLevel.Format),
                _fonts.UniSans.CreateFont(template.User.TimeOnLevel.Guild.FontSize),
                template.User.TimeOnLevel.Guild.Color,
                new PointF(template.User.TimeOnLevel.Guild.Pos.X, template.User.TimeOnLevel.Guild.Pos.Y)));
        //avatar

        if (stats.User.AvatarId != null && template.User.Icon.Show)
            try
            {
                var avatarUrl = stats.User.RealAvatarUrl();

                var (succ, data) = await _cache.TryGetImageDataAsync(avatarUrl);
                if (!succ)
                {
                    using (var http = _httpFactory.CreateClient())
                    {
                        var avatarData = await http.GetByteArrayAsync(avatarUrl);
                        using var tempDraw = Image.Load(avatarData);
                        tempDraw.Mutate(x => x
                                             .Resize(template.User.Icon.Size.X, template.User.Icon.Size.Y)
                                             .ApplyRoundedCorners(Math.Max(template.User.Icon.Size.X,
                                                                      template.User.Icon.Size.Y)
                                                                  / 2));
                        await using var stream = tempDraw.ToStream();
                        data = stream.ToArray();
                    }

                    await _cache.SetImageDataAsync(avatarUrl, data);
                }

                using var toDraw = Image.Load(data);
                if (toDraw.Size() != new Size(template.User.Icon.Size.X, template.User.Icon.Size.Y))
                    toDraw.Mutate(x => x.Resize(template.User.Icon.Size.X, template.User.Icon.Size.Y));

                img.Mutate(x => x.DrawImage(toDraw, new Point(template.User.Icon.Pos.X, template.User.Icon.Pos.Y), 1));
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error drawing avatar image");
            }

        //club image

        img.Mutate(x => x.Resize(template.OutputSize.X, template.OutputSize.Y));
        return (img.ToStream(imageFormat), imageFormat);
    }


private void DrawXpBar(float percent, XpBar info, Image<Rgba32> img)
    {
        var x1 = info.PointA.X;
        var y1 = info.PointA.Y;

        var x2 = info.PointB.X;
        var y2 = info.PointB.Y;

        var length = info.Length * percent;

        float x3 = 0, x4 = 0, y3 = 0, y4 = 0;

        switch (info.Direction)
        {
            case XpTemplateDirection.Down:
                x3 = x1;
                x4 = x2;
                y3 = y1 + length;
                y4 = y2 + length;
                break;
            case XpTemplateDirection.Up:
                x3 = x1;
                x4 = x2;
                y3 = y1 - length;
                y4 = y2 - length;
                break;
            case XpTemplateDirection.Left:
                x3 = x1 - length;
                x4 = x2 - length;
                y3 = y1;
                y4 = y2;
                break;
            default:
                x3 = x1 + length;
                x4 = x2 + length;
                y3 = y1;
                y4 = y2;
                break;
        }

        img.Mutate(x => x.FillPolygon(info.Color, new PointF(x1, y1), new PointF(x3, y3), new PointF(x4, y4),
            new PointF(x2, y2)));
    }


    public void XpReset(ulong guildId, ulong userId)
    {
        using var uow = _db.GetDbContext();
        uow.UserXpStats.ResetGuildUserXp(userId, guildId);
        uow.SaveChanges();
    }

    public void XpReset(ulong guildId)
    {
        using var uow = _db.GetDbContext();
        uow.UserXpStats.ResetGuildXp(guildId);
        uow.SaveChanges();
    }

    private enum NotifOf
    {
        Server,
        Global
    } // is it a server level-up or global level-up notification
}