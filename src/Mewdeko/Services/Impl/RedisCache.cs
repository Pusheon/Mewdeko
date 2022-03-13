using System.Net;
using Mewdeko._Extensions;
using Newtonsoft.Json;
using StackExchange.Redis;
using Mewdeko.Database.Models;
using Mewdeko.Modules.Utility.Common;

namespace Mewdeko.Services.Impl;

public class RedisCache : IDataCache
{
    private readonly EndPoint _redisEndpoint;

    private readonly string _redisKey;

    private readonly object _timelyLock = new();
    private readonly object _voteLock = new();

    public RedisCache(IBotCredentials creds, int shardId)
    {
        var conf = ConfigurationOptions.Parse(creds.RedisOptions);

        Redis = ConnectionMultiplexer.Connect(conf);
        _redisEndpoint = Redis.GetEndPoints().First();
        LocalImages = new RedisImagesCache(Redis, creds);
        LocalData = new RedisLocalDataCache(Redis, creds, shardId);
        _redisKey = creds.RedisKey();
    }

    public ConnectionMultiplexer Redis { get; }

    public IImageCache LocalImages { get; }
    public ILocalDataCache LocalData { get; }

    // things here so far don't need the bot id
    // because it's a good thing if different bots 
    // which are hosted on the same PC
    // can re-use the same image/anime data
    public async Task<(bool Success, byte[] Data)> TryGetImageDataAsync(Uri key)
    {
        var db = Redis.GetDatabase();
        byte[] x = await db.StringGetAsync($"image_{key}").ConfigureAwait(false);
        return (x != null, x);
    }

    public async Task CacheAfk(ulong id, List<AFK> objectList) =>
        await Task.Run(() => 
            new RedisDictionary<ulong, List<AFK>>($"{_redisKey}_afk", Redis) { { id, objectList } });

    public async Task CacheHighlights(ulong id, List<Highlights> objectList) =>
        await Task.Run(() =>
            _ = new RedisDictionary<ulong, List<Highlights>>($"{_redisKey}_Highlights", Redis) { { id, objectList } });

    public async Task CacheHighlightSettings(ulong id, List<HighlightSettings> objectList) =>
        await Task.Run(() =>
        {
            _ = new RedisDictionary<ulong, List<HighlightSettings>>($"{_redisKey}_highlightSettings", Redis)
            {
                { id, objectList }
            };
        });

    public void CacheGuildConfigs(ulong id, List<GuildConfig> objectList) 
        => new RedisDictionary<ulong, List<GuildConfig>>($"{_redisKey}_afk", Redis){{id, objectList}};

    public List<AFK> GetAfkForGuild(ulong id)
    {
        var customers = new RedisDictionary<ulong, List<AFK>>($"{_redisKey}_afk", Redis);
        return customers[id];
    }
    

    public Task AddAfkToCache(ulong id, List<AFK> newAfk)
    {
        var customers = new RedisDictionary<ulong, List<AFK>>($"{_redisKey}_afk", Redis);
        customers.Remove(id);
        customers.Add(id, newAfk);
        return Task.CompletedTask;
    }
    
    public Task AddSnipeToCache(ulong id, List<SnipeStore> newAfk)
    {
        var customers = new RedisDictionary<ulong, List<SnipeStore>>($"{id}_{_redisKey}_snipes", Redis);
        customers.Remove(id);
        customers.Add(id, newAfk);
        return Task.CompletedTask;
    }
    
    public Task AddHighlightToCache(ulong id, List<Highlights> newHighlight)
    {
        var customers = new RedisDictionary<ulong, List<Highlights>>($"{_redisKey}_highlights", Redis);
        customers.Remove(id);
        customers.Add(id, newHighlight);
        return Task.CompletedTask;
    }

    public Task AddIgnoredChannels(ulong guildId, ulong userId, string ignored)
    {
        var db = Redis.GetDatabase();
        db.StringSet($"{_redisKey}_ignoredchannels_{guildId}_{userId}", ignored);
        return Task.CompletedTask;
    }

    public string GetIgnoredChannels(ulong guildId, ulong userId)
    {
        var db = Redis.GetDatabase();
        var value = db.StringGet($"{_redisKey}_ignoredchannels_{guildId}_{userId}");
        return JsonConvert.DeserializeObject<string>(value);
    }
    
    public Task AddIgnoredUsers(ulong guildId, ulong userId, string ignored)
    {
        var db = Redis.GetDatabase();
        db.StringSet($"{_redisKey}_ignoredchannels_{guildId}_{userId}", ignored);
        return Task.CompletedTask;
    }

    public Task<bool> TryAddHighlightStaggerUser(ulong userId)
    {
        var db = Redis.GetDatabase();
        return Task.FromResult(db.StringSet($"{_redisKey}_hstagger_{userId}", 0, TimeSpan.FromMinutes(2), When.NotExists));
    }
    public string GetIgnoredUsers(ulong guildId, ulong userId)
    {
        var db = Redis.GetDatabase();
        var value = db.StringGet($"{_redisKey}_ignoredchannels_{guildId}_{userId}");
        return JsonConvert.DeserializeObject<string>(value);
    }
    public Task RemoveHighlightFromCache(ulong id, List<Highlights> newHighlight)
    {
        var customers = new RedisDictionary<ulong, List<Highlights>>($"{_redisKey}_highlights", Redis);
        customers.Remove(id);
        customers.Add(id, newHighlight);
        return Task.CompletedTask;
    }
    
    public Task AddHighlightSettingToCache(ulong id, List<HighlightSettings> newHighlight)
    {
        var customers = new RedisDictionary<ulong, List<HighlightSettings>>($"{_redisKey}_highlightSettings", Redis);
        customers.Remove(id);
        customers.Add(id, newHighlight);
        return Task.CompletedTask;
    }

#pragma warning disable CS1998
    public async Task<List<SnipeStore>> GetSnipesForGuild(ulong id)
#pragma warning restore CS1998
    {
        var customers = new RedisDictionary<ulong, List<SnipeStore>>($"{id}_{_redisKey}_snipes", Redis);
        return customers[id];
    }

    public List<Highlights> GetHighlightsForGuild(ulong id)
    {
        var customers = new RedisDictionary<ulong, List<Highlights>>($"{_redisKey}_highlights", Redis);
        return customers[id];
    }
    
    public List<HighlightSettings> GetHighlightSettingsForGuild(ulong id)
    {
        var customers = new RedisDictionary<ulong, List<HighlightSettings>>($"{_redisKey}_highlightSettings", Redis);
        return customers[id];
    }

    public async Task SetImageDataAsync(Uri key, byte[] data)
    {
        var db = Redis.GetDatabase();
        await db.StringSetAsync($"image_{key}", data);
    }


    public TimeSpan? AddTimelyClaim(ulong id, int period)
    {
        if (period == 0)
            return null;
        lock (_timelyLock)
        {
            var time = TimeSpan.FromHours(period);
            var db = Redis.GetDatabase();
            if ((bool?) db.StringGet($"{_redisKey}_timelyclaim_{id}") == null)
            {
                db.StringSet($"{_redisKey}_timelyclaim_{id}", true, time);
                return null;
            }

            return db.KeyTimeToLive($"{_redisKey}_timelyclaim_{id}");
        }
    }

    public TimeSpan? AddVoteClaim(ulong id, int period)
    {
        if (period == 0)
            return null;
        lock (_voteLock)
        {
            var time = TimeSpan.FromHours(period);
            var db = Redis.GetDatabase();
            if ((bool?) db.StringGet($"{_redisKey}_voteclaim_{id}") == null)
            {
                db.StringSet($"{_redisKey}_voteclaim_{id}", true, time);
                return null;
            }

            return db.KeyTimeToLive($"{_redisKey}_voteclaim_{id}");
        }
    }

    public void RemoveAllTimelyClaims()
    {
        var server = Redis.GetServer(_redisEndpoint);
        var db = Redis.GetDatabase();
        foreach (var k in server.Keys(pattern: $"{_redisKey}_timelyclaim_*"))
            db.KeyDelete(k, CommandFlags.FireAndForget);
    }

    public bool TryAddAffinityCooldown(ulong userId, out TimeSpan? time)
    {
        var db = Redis.GetDatabase();
        time = db.KeyTimeToLive($"{_redisKey}_affinity_{userId}");
        if (time == null)
        {
            time = TimeSpan.FromMinutes(30);
            db.StringSet($"{_redisKey}_affinity_{userId}", true, time);
            return true;
        }

        return false;
    }

    public bool TryAddDivorceCooldown(ulong userId, out TimeSpan? time)
    {
        var db = Redis.GetDatabase();
        time = db.KeyTimeToLive($"{_redisKey}_divorce_{userId}");
        if (time == null)
        {
            time = TimeSpan.FromHours(6);
            db.StringSet($"{_redisKey}_divorce_{userId}", true, time);
            return true;
        }

        return false;
    }

    public Task<bool> TryAddHighlightStagger(ulong guildId, ulong userId)
    {
        var db = Redis.GetDatabase();
        return Task.FromResult(db.StringSet($"{_redisKey}_hstagger_{guildId}_{userId}", 0, TimeSpan.FromMinutes(10), When.NotExists));
    }

    public Task<bool> GetHighlightStagger(ulong guildId, ulong userId)
    {
        var db = Redis.GetDatabase();
        return Task.FromResult(db.StringGet($"{_redisKey}_hstagger_{guildId}_{userId}").HasValue);
    }
    public TimeSpan? TryAddRatelimit(ulong id, string name, int expireIn)
    {
        var db = Redis.GetDatabase();
        if (db.StringSet($"{_redisKey}_ratelimit_{id}_{name}",
                0, // i don't use the value
                TimeSpan.FromSeconds(expireIn),
                When.NotExists))
            return null;

        return db.KeyTimeToLive($"{_redisKey}_ratelimit_{id}_{name}");
    }

    public bool TryGetEconomy(out string data)
    {
        var db = Redis.GetDatabase();
        if ((data = db.StringGet($"{_redisKey}_economy")) != null) return true;

        return false;
    }

    public void SetEconomy(string data)
    {
        var db = Redis.GetDatabase();
        db.StringSet($"{_redisKey}_economy",
            data,
            TimeSpan.FromMinutes(3));
    }
    public async Task SetGuildSettingBool(ulong guildId, string setting, bool value)
    {
        var db = Redis.GetDatabase();
        await db.StringSetAsync($"{_redisKey}_{setting}_{guildId}", JsonConvert.SerializeObject(value));
    }
    
    public async Task<bool> GetGuildSettingBool(ulong guildId, string setting)
    {
        var db = Redis.GetDatabase();
        var toget = await db.StringGetAsync($"{_redisKey}_{setting}_{guildId}");
        return JsonConvert.DeserializeObject<bool>(toget);
    }
    public async Task SetGuildSettingInt(ulong guildId, string setting, int value)
    {
        var db = Redis.GetDatabase();
        await db.StringSetAsync($"{_redisKey}_{setting}_{guildId}", JsonConvert.SerializeObject(value));
    }
    
    public async Task<int> GetGuildSettingInt(ulong guildId, string setting)
    {
        var db = Redis.GetDatabase();
        var toget = await db.StringGetAsync($"{_redisKey}_{setting}_{guildId}");
        return JsonConvert.DeserializeObject<int>(toget);
    }
    
    public async Task SetGuildSettingString(ulong guildId, string setting, string value)
    {
        var db = Redis.GetDatabase();
        await db.StringSetAsync($"{_redisKey}_{setting}_{guildId}", JsonConvert.SerializeObject(value));
    }
    
    public async Task<string> GetGuildSettingString(ulong guildId, string setting)
    {
        var db = Redis.GetDatabase();
        var toget = await db.StringGetAsync($"{_redisKey}_{setting}_{guildId}");
        return JsonConvert.DeserializeObject<string>(toget);
    }
    public async Task<TOut> GetOrAddCachedDataAsync<TParam, TOut>(string key, Func<TParam, Task<TOut>> factory,
        TParam param, TimeSpan expiry) where TOut : class
    {
        var db = Redis.GetDatabase();

        var data = await db.StringGetAsync(key).ConfigureAwait(false);
        if (!data.HasValue)
        {
            var obj = await factory(param).ConfigureAwait(false);

            if (obj == null)
                return default;

            await db.StringSetAsync(key, JsonConvert.SerializeObject(obj),
                expiry).ConfigureAwait(false);

            return obj;
        }

        return (TOut) JsonConvert.DeserializeObject(data, typeof(TOut));
    }

    public DateTime GetLastCurrencyDecay()
    {
        var db = Redis.GetDatabase();

        var str = (string) db.StringGet($"{_redisKey}_last_currency_decay");
        if (string.IsNullOrEmpty(str))
            return DateTime.MinValue;

        return JsonConvert.DeserializeObject<DateTime>(str);
    }

    public void SetLastCurrencyDecay()
    {
        var db = Redis.GetDatabase();

        db.StringSet($"{_redisKey}_last_currency_decay", JsonConvert.SerializeObject(DateTime.UtcNow));
    }

    public Task SetStreamDataAsync(string url, string data)
    {
        var db = Redis.GetDatabase();
        return db.StringSetAsync($"{_redisKey}_stream_{url}", data, TimeSpan.FromHours(6));
    }

    public bool TryGetStreamData(string url, out string dataStr)
    {
        var db = Redis.GetDatabase();
        dataStr = db.StringGet($"{_redisKey}_stream_{url}");

        return !string.IsNullOrWhiteSpace(dataStr);
    }
}