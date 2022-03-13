global using System;
global using System.Linq;
global using Mewdeko.Services;
global using System.Threading.Tasks;
global using System.Collections;
global using System.Collections.Generic;
using Serilog;
using System.IO;
using System.Net.Http;

var pid = Environment.ProcessId;

var shardId = 0;
if (args.Length > 0)
{
    if (!int.TryParse(args[0], out shardId))
    {
        Console.Error.WriteLine("Invalid first argument (shard id): {0}", args[0]);
        return;
    }

    if (args.Length > 1)
    {
        if (!int.TryParse(args[1], out _))
        {
            Console.Error.WriteLine("Invalid second argument (total shards): {0}", args[1]);
            return;
        }
    }
}
if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "data/Mewdeko.db")))
{
    var uri = new Uri("https://cdn.discordapp.com/attachments/915770282579484693/946967102680621087/Mewdeko.db");
    var client = new HttpClient();
    var response = client.GetAsync(uri).Result;
    await using var fs = new FileStream(
        Path.Combine(AppContext.BaseDirectory, "data/Mewdeko.db"), 
        FileMode.CreateNew);
    await response.Content.CopyToAsync(fs);
}
Environment.SetEnvironmentVariable($"AFK_CACHED_{shardId}", "0");

LogSetup.SetupLogger(shardId);
Log.Information($"Pid: {pid}");
await new Mewdeko.Mewdeko(shardId).RunAndBlockAsync();