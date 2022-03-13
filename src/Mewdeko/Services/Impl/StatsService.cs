using Discord.Rest;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using Discord.WebSocket;
using Humanizer.Bytes;
using Mewdeko.Modules.Utility.Services;
using Serilog;
using System.Net.Http;

namespace Mewdeko.Services.Impl;

public class StatsService : IStatsService
{
    public DiscordSocketClient Client { get; }
    public IHttpClientFactory Factory { get; }
    public IBotCredentials Creds { get; }
    public ICoordinator Coord { get; }
    public const string BOT_VERSION = "5.01";
    

    private readonly DateTime _started;

    public StatsService(
        DiscordSocketClient client, IHttpClientFactory factory, IBotCredentials creds, ICoordinator coord)
    {
        Client = client;
        Factory = factory;
        Creds = creds;
        Coord = coord;
        _ = new DllVersionChecker();
        _started = DateTime.UtcNow;
        _ = PostToTopGg();
    }
    
    public string Library => $"Discord.Net {DllVersionChecker.GetDllVersion()} ";

    public string Heap => ByteSize.FromBytes(Process.GetCurrentProcess().PrivateMemorySize64).Megabytes
        .ToString(CultureInfo.InvariantCulture);
    
    

    private TimeSpan GetUptime() => DateTime.UtcNow - _started;

    public string GetUptimeString(string separator = ", ")
    {
        var time = GetUptime();
        return $"{time.Days} days{separator}{time.Hours} hours{separator}{time.Minutes} minutes";
    }

    public async Task PostToTopGg()
    {
        if (Client.ShardId != 0) 
            return;
        
        if (Client.CurrentUser.Id != 752236274261426212)
            return;
        var timer = new PeriodicTimer(TimeSpan.FromMinutes(10));
        while (await timer.WaitForNextTickAsync())
        {
            try
            {
                
                using var http = Factory.CreateClient();
                using var content = new FormUrlEncodedContent(
                    new Dictionary<string, string>
                    {
                        {"shard_count", Creds.TotalShards.ToString()},
                        {"shard_id", Client.ShardId.ToString()},
                        {"server_count", Coord.GetGuildCount().ToString()}
                    });
                content.Headers.Clear();
                content.Headers.Add("Content-Type", "application/x-www-form-urlencoded");
                http.DefaultRequestHeaders.Add("Authorization",
                    Creds.VotesToken);

                using (await http
                             .PostAsync(new Uri($"https://top.gg/api/bots/{Client.CurrentUser.Id}/stats"),
                                 content).ConfigureAwait(false))
                {
                }
                var chan = (await Client.Rest.GetChannelAsync(934661783480832000)) as RestTextChannel;
                await chan.SendMessageAsync("Sent count to top.gg!");
            }
            catch (Exception ex)
            {
                Log.Error(ex.ToString());
                // ignored
            }
        }
    }
}
