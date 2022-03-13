using System.IO;
using System.Net.Http;
using Newtonsoft.Json.Linq;
using Serilog;
using StackExchange.Redis;

namespace Mewdeko.Services.Common;

public class ImageLoader
{
    private readonly ConnectionMultiplexer _con;
    private readonly HttpClient _http;

    private readonly List<Task<KeyValuePair<RedisKey, RedisValue>>> _uriTasks = new();

    public ImageLoader(HttpClient http, ConnectionMultiplexer con, Func<string, RedisKey> getKey)
    {
        _http = http;
        _con = con;
        GetKey = getKey;
    }

    public Func<string, RedisKey> GetKey { get; }

    private IDatabase Db => _con.GetDatabase();

    private async Task<byte[]> GetImageData(Uri uri)
    {
        if (uri.IsFile)
            try
            {
                var bytes = await File.ReadAllBytesAsync(uri.LocalPath);
                return bytes;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed reading image bytes");
                return null;
            }

        return await _http.GetByteArrayAsync(uri);
    }

    private async Task HandleJArray(JArray arr, string key)
    {
        var tasks = arr.Where(x => x.Type == JTokenType.String)
            .Select(async x =>
            {
                try
                {
                    return await GetImageData((Uri) x).ConfigureAwait(false);
                }
                catch
                {
                    Log.Error("Error retreiving image for key {Key}: {Data}", key, x);
                    return null;
                }
            });

        var vals = Array.Empty<byte[]>();
        vals = await Task.WhenAll(tasks).ConfigureAwait(false);
        if (vals.Any(x => x == null))
            vals = vals.Where(x => x != null).ToArray();

        await Db.KeyDeleteAsync(GetKey(key)).ConfigureAwait(false);
        await Db.ListRightPushAsync(GetKey(key),
            vals.Where(x => x != null)
                .Select(x => (RedisValue) x)
                .ToArray()).ConfigureAwait(false);

        if (arr.Count != vals.Length)
            Log.Information(
                "{2}/{1} URIs for the key '{0}' have been loaded. Some of the supplied URIs are either unavailable or invalid.",
                key, arr.Count, vals.Length);
    }

    private async Task<KeyValuePair<RedisKey, RedisValue>> HandleUri(Uri uri, string key)
    {
        try
        {
            RedisValue data = await GetImageData(uri).ConfigureAwait(false);
            return new KeyValuePair<RedisKey, RedisValue>(GetKey(key), data);
        }
        catch
        {
            Log.Information("Setting '{0}' image failed. The URI you provided is either unavailable or invalid.",
                key.ToLowerInvariant());
            return new KeyValuePair<RedisKey, RedisValue>("", "");
        }
    }

    private Task HandleJObject(JObject obj, string parent = "")
    {
        string GetParentString()
        {
            if (string.IsNullOrWhiteSpace(parent))
                return "";
            return $"{parent}_";
        }

        var tasks = new List<Task>();
        Task t;
        // go through all of the kvps in the object
        foreach (var kvp in obj)
            switch (kvp.Value.Type)
            {
                // if it's a JArray, resole it using jarray method which will
                // return task<byte[][]> aka an array of all images' bytes
                case JTokenType.Array:
                    t = HandleJArray((JArray) kvp.Value, GetParentString() + kvp.Key);
                    tasks.Add(t);
                    break;
                case JTokenType.String:
                    {
                        var uriTask = HandleUri((Uri) kvp.Value, GetParentString() + kvp.Key);
                        _uriTasks.Add(uriTask);
                        break;
                    }
                case JTokenType.Object:
                    t = HandleJObject((JObject) kvp.Value, GetParentString() + kvp.Key);
                    tasks.Add(t);
                    break;
            }

        return Task.WhenAll(tasks);
    }

    public async Task LoadAsync(JObject obj)
    {
        await HandleJObject(obj).ConfigureAwait(false);
        var results = await Task.WhenAll(_uriTasks).ConfigureAwait(false);
        await Db.StringSetAsync(results.Where(x => x.Key != "").ToArray()).ConfigureAwait(false);
    }
}