using Discord.Commands;
using Discord.Interactions;
using Discord.Net;
using Discord.Rest;
using Fergun.Interactive;
using Lavalink4NET;
using Lavalink4NET.DiscordNet;
using MartineApiNet;
using Mewdeko.Common.Configs;
using Mewdeko.Common.ModuleBehaviors;
using Mewdeko.Common.PubSub;
using Mewdeko.Common.TypeReaders;
using Mewdeko.Modules.Gambling.Services;
using Mewdeko.Modules.Gambling.Services.Impl;
using Mewdeko.Modules.Music.Services;
using Mewdeko.Modules.Searches.Services;
using Mewdeko.Services.Impl;
using Microsoft.Extensions.DependencyInjection;
using NekosBestApiNet;
using Newtonsoft.Json;
using Serilog;
using StackExchange.Redis;
using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using RunMode = Discord.Commands.RunMode;
using TypeReader = Discord.Commands.TypeReader;

namespace Mewdeko;

public class Mewdeko
{
    private readonly DbService _db;

    public Mewdeko(int shardId)
    {
        if (shardId < 0)
            throw new ArgumentOutOfRangeException(nameof(shardId));

        Credentials = new BotCredentials();
        Cache = new RedisCache(Credentials, shardId);
        _db = new DbService(Credentials.TotalShards);

        if (shardId == 0) _db.Setup();

        Client = new DiscordSocketClient(new DiscordSocketConfig
        {
            MessageCacheSize = 15,
            LogLevel = LogSeverity.Verbose,
            ConnectionTimeout = int.MaxValue,
            TotalShards = Credentials.TotalShards,
            ShardId = shardId,
            AlwaysDownloadUsers = true,
            GatewayIntents = GatewayIntents.All,
            LogGatewayIntentWarnings = false,
            FormatUsersInBidirectionalUnicode = false

        });
        CommandService = new CommandService(new CommandServiceConfig
        {
            CaseSensitiveCommands = false,
            DefaultRunMode = RunMode.Async,
            
        });
    }

    public BotCredentials Credentials { get; }
    public DiscordSocketClient Client { get; }
    private CommandService CommandService { get; }

    public static Color OkColor { get; set; }
    public static Color ErrorColor { get; set; }

    public TaskCompletionSource<bool> Ready { get; } = new();

    private IServiceProvider Services { get; set; }
    private IDataCache Cache { get; }

    public event Func<GuildConfig, Task> JoinedGuild = delegate { return Task.CompletedTask; };

    public List<ulong> GetCurrentGuildIds() => Client.Guilds.Select(x => x.Id).ToList();

    public GuildConfig GetGuildConfig(ulong guildId)
    {
        using var uow = _db.GetDbContext();
        var cachedConfig = Cache.GetGuildConfig(guildId);
        return cachedConfig ?? uow.ForGuildId(guildId);
    }

    public void UpdateGuildConfig(ulong guildId, GuildConfig config)
        => Cache.AddOrUpdateGuildConfig(guildId, config);

    private void AddServices()
    {
        var startingGuildIdList = GetCurrentGuildIds();
        var sw = Stopwatch.StartNew();
        var gs2 = Stopwatch.StartNew();
        var bot = Client.CurrentUser;

        using (var uow = _db.GetDbContext())
        {
            foreach (var config in (IEnumerable<GuildConfig>)uow.GuildConfigs.All().Where(x => startingGuildIdList.Contains(x.GuildId)))
            {
                UpdateGuildConfig(config.GuildId, config);
            }
            uow.EnsureUserCreated(bot.Id, bot.Username, bot.Discriminator, bot.AvatarId);
            gs2.Stop();
            Log.Information($"Guild Configs cached in {gs2.Elapsed.TotalSeconds:F2}s.");
        }

        var s = new ServiceCollection()
                .AddSingleton<IBotCredentials>(Credentials)
                .AddSingleton(_db)
                .AddSingleton(Client)
                .AddSingleton(CommandService)
                .AddSingleton(this)
                .AddSingleton(Cache)
                .AddSingleton(new MartineApi())
                .AddSingleton(Cache.Redis)
                .AddSingleton<ISeria, JsonSeria>()
                .AddSingleton<IPubSub, RedisPubSub>()
                .AddSingleton<IConfigSeria, YamlSeria>()
                .AddSingleton<InteractiveService>()
                .AddSingleton(new NekosBestApi())
                .AddSingleton<InteractionService>()
                .AddSingleton<Localization>()
                .AddSingleton<MusicService>()
                .AddConfigServices()
                .AddBotStringsServices(Credentials.TotalShards)
                .AddMemoryCache()
                .AddSingleton<IDiscordClientWrapper, DiscordClientWrapper>()
                .AddSingleton<IAudioService, LavalinkNode>()
                .AddSingleton<LavalinkNode>()
                .AddSingleton(new LavalinkNodeOptions
                {
                    Password = "Hope4a11",
                    WebSocketUri = "ws://127.0.0.1:2333",
                    RestUri = "http://127.0.0.1:2333",
                    DisconnectOnStop = false,
                })
                .AddSingleton<IShopService, ShopService>()
                .AddSingleton<ToneTagService>();

        s.AddHttpClient();
        s.AddHttpClient("memelist").ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AllowAutoRedirect = false
        });
        if (Environment.GetEnvironmentVariable("MEWDEKO_IS_COORDINATED") != "1")
        {
            s.AddSingleton<ICoordinator, SingleProcessCoordinator>();
        }
        else
        {
            s.AddSingleton<RemoteGrpcCoordinator>()
                        .AddSingleton<ICoordinator>(x => x.GetRequiredService<RemoteGrpcCoordinator>())
                        .AddSingleton<IReadyExecutor>(x => x.GetRequiredService<RemoteGrpcCoordinator>());
        }

        s.Scan(scan => scan.FromAssemblyOf<IReadyExecutor>()
                                  .AddClasses(classes => classes.AssignableToAny(
                                      // services
                                      typeof(INService),
                                      // behaviours
                                      typeof(IEarlyBehavior),
                                      typeof(ILateBlocker),
                                      typeof(IInputTransformer),
                                      typeof(ILateExecutor)))
                                  .AsSelfWithInterfaces()
                                  .WithSingletonLifetime()
        );

        s.LoadFrom(Assembly.GetAssembly(typeof(CommandHandler))!);
        //initialize Services
        Services = s.BuildServiceProvider();
        var commandHandler = Services.GetService<CommandHandler>();
        commandHandler.AddServices(s);
        _ = Task.Run(() => LoadTypeReaders(typeof(Mewdeko).Assembly));

        sw.Stop();
        Log.Information($"All services loaded in {sw.Elapsed.TotalSeconds:F2}s");
    }

    private IEnumerable<object> LoadTypeReaders(Assembly assembly)
    {
        var sw = new Stopwatch();
        sw.Start();
        var interactionService = Services.GetService<InteractionService>();
        Type[] allTypes;
        try
        {
            allTypes = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            Log.Warning(ex.LoaderExceptions[0], "Error getting types");
            return Enumerable.Empty<object>();
        }

        var filteredTypes = allTypes
            .Where(x => x.IsSubclassOf(typeof(TypeReader))
                        && x.BaseType.GetGenericArguments().Length > 0
                        && !x.IsAbstract);

        var toReturn = new List<object>();
        foreach (var ft in filteredTypes)
        {
            var x = (TypeReader)ActivatorUtilities.CreateInstance(Services, ft);
            var baseType = ft.BaseType;
            var typeArgs = baseType?.GetGenericArguments();
            if (typeArgs != null) CommandService.AddTypeReader(typeArgs[0], x);
            toReturn.Add(x);
        }

        CommandService.AddTypeReaders<IEmote>(
            new TryParseTypeReader<Emote>(Emote.TryParse),
            new TryParseTypeReader<Emoji>(Emoji.TryParse));

        interactionService.AddTypeConverter<TimeSpan>(new TimeSpanConverter());
        sw.Stop();
        Log.Information($"TypeReaders loaded in {sw.Elapsed.TotalSeconds:F2}s");
        return toReturn;
    }

    private async Task LoginAsync(string token)
    {
        Client.Log += Client_Log;
        var clientReady = new TaskCompletionSource<bool>();

        Task SetClientReady()
        {
            var _ = Task.Run(() => clientReady.TrySetResult(true));
            return Task.CompletedTask;
        }

        //connect
        Log.Information("Shard {0} logging in ...", Client.ShardId);
        try
        {
            await Client.LoginAsync(TokenType.Bot, token).ConfigureAwait(false);
            await Client.StartAsync().ConfigureAwait(false);
        }
        catch (HttpException ex)
        {
            LoginErrorHandler.Handle(ex);
            Helpers.ReadErrorAndExit(3);
        }
        catch (Exception ex)
        {
            LoginErrorHandler.Handle(ex);
            Helpers.ReadErrorAndExit(4);
        }

        Client.Ready += SetClientReady;
        await clientReady.Task.ConfigureAwait(false);
        Client.Ready -= SetClientReady;
        Client.JoinedGuild += Client_JoinedGuild;
        Client.LeftGuild += Client_LeftGuild;
        Log.Information("Shard {0} logged in.", Client.ShardId);

#if !DEBUG
        Client.Log -= Client_Log;
#endif
    }

    private Task Client_LeftGuild(SocketGuild arg)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var chan = await Client.Rest.GetChannelAsync(Credentials.GuildJoinsChannelId).ConfigureAwait(false);
                await ((RestTextChannel)chan).SendErrorAsync($"Left server: {arg.Name} [{arg.Id}]");
                if (arg.Name is not null)
                {
                    Cache.DeleteGuildConfig(arg.Id);
                }
            }
            catch
            {
                //ignored
            }

            Log.Information("Left server: {0} [{1}]", arg.Name, arg.Id);
        });
        return Task.CompletedTask;
    }

    private Task Client_JoinedGuild(SocketGuild arg)
    {
        _ = Task.Run(async () =>
        {
            await arg.DownloadUsersAsync();
            Log.Information("Joined server: {0} [{1}]", arg.Name, arg.Id);

            GuildConfig gc;
            await using (var uow = _db.GetDbContext())
            {
                gc = uow.ForGuildId(arg.Id);
            }

            Cache.AddOrUpdateGuildConfig(arg.Id, gc);
            await JoinedGuild.Invoke(gc).ConfigureAwait(false);
            var chan = await Client.Rest.GetChannelAsync(Credentials.GuildJoinsChannelId) as RestTextChannel;
            var eb = new EmbedBuilder();
            eb.WithTitle($"Joined {Format.Bold(arg.Name)} {arg.Id}");
            eb.AddField("Members", arg.MemberCount);
            eb.AddField("Boosts", arg.PremiumSubscriptionCount);
            eb.AddField("Owner", $"Name: {arg.Owner}\nID: {arg.OwnerId}");
            eb.AddField("Text Channels", arg.TextChannels.Count);
            eb.AddField("Voice Channels", arg.VoiceChannels.Count);
            eb.WithThumbnailUrl(arg.IconUrl);
            eb.WithColor(OkColor);
            await chan.SendMessageAsync(embed: eb.Build());
        });
        return Task.CompletedTask;
    }

    private async Task RunAsync()
    {
        var sw = Stopwatch.StartNew();

        await LoginAsync(Credentials.Token).ConfigureAwait(false);

        Log.Information("Shard {ShardId} loading services...", Client.ShardId);
        try
        {
            AddServices();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error adding services");
            Helpers.ReadErrorAndExit(9);
        }

        sw.Stop();
        Log.Information("Shard {ShardId} connected in {Elapsed:F2}s", Client.ShardId, sw.Elapsed.TotalSeconds);
        var commandService = Services.GetService<CommandService>();
        var interactionService = Services.GetRequiredService<InteractionService>();
        await commandService.AddModulesAsync(GetType().GetTypeInfo().Assembly, Services)
                             .ConfigureAwait(false);
        await interactionService.AddModulesAsync(GetType().GetTypeInfo().Assembly, Services)
            .ConfigureAwait(false);
        var lava = Services.GetRequiredService<LavalinkNode>();
        try
        {
            await lava.InitializeAsync();
        }
        catch
        {
            Log.Information("Unable to connect to lavalink. If you want music please launch tha lavalink binary separately.");
        }
#if  !DEBUG
        if (Client.ShardId == 0)
            await interactionService.RegisterCommandsGloballyAsync();
#endif
#if DEBUG
        if (GetCurrentGuildIds().Contains(Credentials.DebugGuildId))
            await interactionService.RegisterCommandsToGuildAsync(Credentials.DebugGuildId);
#endif

        // start handling messages received in commandhandler

        HandleStatusChanges();
        Ready.TrySetResult(true);
        _ = Task.Run(ExecuteReadySubscriptions);
        Log.Information("Shard {ShardId} ready", Client.ShardId);
    }

    private Task ExecuteReadySubscriptions()
    {
        var readyExecutors = Services.GetServices<IReadyExecutor>();
        var tasks = readyExecutors.Select(async toExec =>
        {
            try
            {
                await toExec.OnReadyAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed running OnReadyAsync method on {Type} type: {Message}", toExec.GetType().Name, ex.Message);
            }
        });

        return tasks.WhenAll();
    }

    private static Task Client_Log(LogMessage arg)
    {
        if (arg.Exception != null)
            Log.Warning(arg.Exception, arg.Source + " | " + arg.Message);
        else
            Log.Warning(arg.Source + " | " + arg.Message);

        return Task.CompletedTask;
    }

    public async Task RunAndBlockAsync()
    {
        await RunAsync().ConfigureAwait(false);
        await Task.Delay(-1).ConfigureAwait(false);
    }

    private void HandleStatusChanges()
    {
        var sub = Services.GetService<IDataCache>()!.Redis.GetSubscriber();
        // ReSharper disable once AsyncVoidLambda
        sub.Subscribe($"{Client.CurrentUser.Id}_status.game_set", async (_, game) =>
        {
            try
            {
                var obj = new { Name = default(string), Activity = ActivityType.Playing };
                obj = JsonConvert.DeserializeAnonymousType(game, obj);
                await Client.SetGameAsync(obj.Name, type: obj.Activity).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error setting game");
            }
        }, CommandFlags.FireAndForget);

        // ReSharper disable once AsyncVoidLambda
        sub.Subscribe($"{Client.CurrentUser.Id}_status.stream_set", async (_, streamData) =>
        {
            try
            {
                var obj = new { Name = "", Url = "" };
                obj = JsonConvert.DeserializeAnonymousType(streamData, obj);
                await Client.SetGameAsync(obj?.Name, obj!.Url, ActivityType.Streaming).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error setting stream");
            }
        }, CommandFlags.FireAndForget);
    }

    public async Task SetGameAsync(string game, ActivityType type)
    {
        var obj = new { Name = game, Activity = type };
        var sub = Services.GetService<IDataCache>()!.Redis.GetSubscriber();
        await sub.PublishAsync($"{Client.CurrentUser.Id}_status.game_set", JsonConvert.SerializeObject(obj));
    }
}
