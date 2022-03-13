using Mewdeko.Database.Models;
using Mewdeko.Modules.Utility.Common;
using StackExchange.Redis;

namespace Mewdeko.Services;

public interface IDataCache
{
    ConnectionMultiplexer Redis { get; }
    IImageCache LocalImages { get; }
    ILocalDataCache LocalData { get; }

    Task CacheAfk(ulong id, List<AFK> objectList);
    List<AFK> GetAfkForGuild(ulong id);
    Task<bool> TryAddHighlightStagger(ulong guildId, ulong userId);
    Task<bool> GetHighlightStagger(ulong guildId, ulong userId);
    Task AddAfkToCache(ulong id, List<AFK> newAfk);
    Task CacheHighlights(ulong id, List<Highlights> highlights);
    Task CacheHighlightSettings(ulong id, List<HighlightSettings> highlightSettings);
    Task AddHighlightToCache(ulong id, List<Highlights> newHighlight);
    Task RemoveHighlightFromCache(ulong id, List<Highlights> newHighlight);
    Task AddHighlightSettingToCache(ulong id, List<HighlightSettings> newHighlightSetting);
    Task<bool> TryAddHighlightStaggerUser(ulong id);
    List<Highlights> GetHighlightsForGuild(ulong id);
    List<HighlightSettings> GetHighlightSettingsForGuild(ulong id);
    Task<List<SnipeStore>> GetSnipesForGuild(ulong id);
    Task SetGuildSettingInt(ulong guildId, string setting, int value);
    Task<int> GetGuildSettingInt(ulong guildId, string setting);
    Task AddSnipeToCache(ulong id, List<SnipeStore> newAfk);
    Task SetGuildSettingString(ulong guildId, string setting, string value);
    Task<string> GetGuildSettingString(ulong guildId, string setting);
    Task SetGuildSettingBool(ulong guildId, string setting, bool value);
    Task<bool> GetGuildSettingBool(ulong guildId, string setting);
    Task<(bool Success, byte[] Data)> TryGetImageDataAsync(Uri key);
    Task SetImageDataAsync(Uri key, byte[] data);
    TimeSpan? AddTimelyClaim(ulong id, int period);
    TimeSpan? AddVoteClaim(ulong id, int period);
    TimeSpan? TryAddRatelimit(ulong id, string name, int expireIn);
    void RemoveAllTimelyClaims();
    bool TryAddAffinityCooldown(ulong userId, out TimeSpan? time);
    bool TryAddDivorceCooldown(ulong userId, out TimeSpan? time);
    bool TryGetEconomy(out string data);
    void SetEconomy(string data);
    

    Task<TOut> GetOrAddCachedDataAsync<TParam, TOut>(string key, Func<TParam, Task<TOut>> factory, TParam param,
        TimeSpan expiry) where TOut : class;

    DateTime GetLastCurrencyDecay();
    void SetLastCurrencyDecay();
}