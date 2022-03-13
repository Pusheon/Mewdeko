using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Mewdeko._Extensions;
using Mewdeko.Modules.Administration.Services;

namespace Mewdeko.Common.Replacements;

public class ReplacementBuilder
{
    private static readonly Regex _rngRegex =
        new("%rng(?:(?<from>(?:-)?\\d+)-(?<to>(?:-)?\\d+))?%", RegexOptions.Compiled);

    private readonly DiscordSocketClient _client;

    private readonly ConcurrentDictionary<Regex, Func<Match, string>> _regex = new();
    private readonly ConcurrentDictionary<string, Func<string>> _reps = new();

    public ReplacementBuilder(DiscordSocketClient? client = null)
    {
        _client = client;
        WithRngRegex();
    }

    public ReplacementBuilder WithDefault(IUser usr, IMessageChannel ch, SocketGuild g, DiscordSocketClient client) =>
        WithUser(usr)
            .WithChannel(ch)
            .WithServer(client, g)
            .WithClient(client);

    public ReplacementBuilder WithDefault(ICommandContext ctx) => WithDefault(ctx.User, ctx.Channel, ctx.Guild as SocketGuild, (DiscordSocketClient) ctx.Client);

    public ReplacementBuilder WithMention(DiscordSocketClient client)
    {
        /*OBSOLETE*/
        _reps.TryAdd("%mention%", () => $"<@{client.CurrentUser.Id}>");
        /*NEW*/
        _reps.TryAdd("%bot.mention%", () => client.CurrentUser.Mention);
        return this;
    }

    public ReplacementBuilder WithClient(DiscordSocketClient client)
    {
        WithMention(client);

        /*OBSOLETE*/
        _reps.TryAdd("%shardid%", () => client.ShardId.ToString());
        _reps.TryAdd("%time%",
            () => DateTime.Now.ToString($"HH:mm {TimeZoneInfo.Local.StandardName.GetInitials()}"));

        /*NEW*/
        _reps.TryAdd("%bot.status%", () => client.Status.ToString());
        _reps.TryAdd("%bot.latency%", () => client.Latency.ToString());
        _reps.TryAdd("%bot.name%", () => client.CurrentUser.Username);
        _reps.TryAdd("%bot.fullname%", () => client.CurrentUser.ToString());
        _reps.TryAdd("%bot.time%",
            () => DateTime.Now.ToString($"HH:mm {TimeZoneInfo.Local.StandardName.GetInitials()}"));
        _reps.TryAdd("%bot.discrim%", () => client.CurrentUser.Discriminator);
        _reps.TryAdd("%bot.id%", () => client.CurrentUser.Id.ToString());
        _reps.TryAdd("%bot.avatar%", () => client.CurrentUser.RealAvatarUrl()?.ToString());

        WithStats(client);
        return this;
    }

    public ReplacementBuilder WithServer(DiscordSocketClient client, SocketGuild? g)
    {
        /*OBSOLETE*/
        _reps.TryAdd("%sid%", () => g == null ? "DM" : g.Id.ToString());
        _reps.TryAdd("%server%", () => g == null ? "DM" : g.Name);
        _reps.TryAdd("%members%", () => g is { } sg ? sg.MemberCount.ToString() : "?");
        _reps.TryAdd("%server_time%", () =>
        {
            var to = TimeZoneInfo.Local;
            if (g != null)
                if (GuildTimezoneService.AllServices.TryGetValue(client.CurrentUser.Id, out var tz))
                    to = tz.GetTimeZoneOrDefault(g.Id) ?? TimeZoneInfo.Local;

            return TimeZoneInfo.ConvertTime(DateTime.UtcNow,
                TimeZoneInfo.Utc,
                to).ToString("HH:mm ") + to.StandardName.GetInitials();
        });
        /*NEW*/
        _reps.TryAdd("%server.icon%", () => g == null ? "DM" : $"{g.IconUrl}?size=2048");
        _reps.TryAdd("%server.id%", () => g == null ? "DM" : g.Id.ToString());
        _reps.TryAdd("%server.name%", () => g == null ? "DM" : g.Name);
        _reps.TryAdd("%server.boostlevel%", () =>
        {
            var e = g.PremiumTier.ToString();
            return e.StartsWith("Tier") ? e.Replace("Tier", "") : "0";
        });
        _reps.TryAdd("%server.boostcount%", () => g.PremiumSubscriptionCount.ToString());
        _reps.TryAdd("%server.members%", () => g is { } sg ? sg.Users.Count.ToString() : "?");
        _reps.TryAdd("%server.members.online%",
            () => g is { } sg ? sg.Users.Count(x => x.Status == UserStatus.Online).ToString() : "?");
        _reps.TryAdd("%server.members.offline%",
            () => g is { } sg ? sg.Users.Count(x => x.Status == UserStatus.Offline).ToString() : "?");
        _reps.TryAdd("%server.members.dnd%",
            () => g is { } sg ? sg.Users.Count(x => x.Status == UserStatus.DoNotDisturb).ToString() : "?");
        _reps.TryAdd("%server.members.idle%",
            () => g is { } sg ? sg.Users.Count(x => x.Status == UserStatus.Idle).ToString() : "?");
        _reps.TryAdd("%server.timestamp.longdatetime%", () =>
        {
            var to = TimeZoneInfo.Local;
            if (g != null)
                if (GuildTimezoneService.AllServices.TryGetValue(client.CurrentUser.Id, out var tz))
                    to = tz.GetTimeZoneOrDefault(g.Id) ?? TimeZoneInfo.Local;

            return TimestampTag.FromDateTime(TimeZoneInfo.ConvertTime(DateTime.UtcNow,
                TimeZoneInfo.Utc,
                to), TimestampTagStyles.LongDateTime).ToString();
        });
        _reps.TryAdd("%server.timestamp.longtime%", () =>
        {
            var to = TimeZoneInfo.Local;
            if (g != null)
                if (GuildTimezoneService.AllServices.TryGetValue(client.CurrentUser.Id, out var tz))
                    to = tz.GetTimeZoneOrDefault(g.Id) ?? TimeZoneInfo.Local;

            return TimestampTag.FromDateTime(TimeZoneInfo.ConvertTime(DateTime.UtcNow,
                TimeZoneInfo.Utc,
                to), TimestampTagStyles.LongTime).ToString();
        });
        _reps.TryAdd("%server.timestamp.longdate%", () =>
        {
            var to = TimeZoneInfo.Local;
            if (g != null)
                if (GuildTimezoneService.AllServices.TryGetValue(client.CurrentUser.Id, out var tz))
                    to = tz.GetTimeZoneOrDefault(g.Id) ?? TimeZoneInfo.Local;

            return TimestampTag.FromDateTime(TimeZoneInfo.ConvertTime(DateTime.UtcNow,
                TimeZoneInfo.Utc,
                to), TimestampTagStyles.LongDate).ToString();
        });
        _reps.TryAdd("%server.timestamp.shortdatetime%", () =>
        {
            var to = TimeZoneInfo.Local;
            if (g != null)
                if (GuildTimezoneService.AllServices.TryGetValue(client.CurrentUser.Id, out var tz))
                    to = tz.GetTimeZoneOrDefault(g.Id) ?? TimeZoneInfo.Local;

            return TimestampTag.FromDateTime(TimeZoneInfo.ConvertTime(DateTime.UtcNow,
                TimeZoneInfo.Utc,
                to)).ToString();
        });
        _reps.TryAdd("%server.time%", () =>
        {
            var to = TimeZoneInfo.Local;
            if (g != null)
                if (GuildTimezoneService.AllServices.TryGetValue(client.CurrentUser.Id, out var tz))
                    to = tz.GetTimeZoneOrDefault(g.Id) ?? TimeZoneInfo.Local;

            return TimeZoneInfo.ConvertTime(DateTime.UtcNow,
                TimeZoneInfo.Utc,
                to).ToString("HH:mm ") + to.StandardName.GetInitials();
        });
        return this;
    }

    public ReplacementBuilder WithChannel(IMessageChannel? ch)
    {
        /*OBSOLETE*/
        _reps.TryAdd("%channel%", () => (ch as ITextChannel)?.Mention ?? $"#{ch.Name}");
        _reps.TryAdd("%chname%", () => ch.Name);
        _reps.TryAdd("%cid%", () => ch?.Id.ToString());
        /*NEW*/
        _reps.TryAdd("%channel.mention%", () => (ch as ITextChannel)?.Mention ?? $"#{ch.Name}");
        _reps.TryAdd("%channel.name%", () => ch.Name);
        _reps.TryAdd("%channel.id%", () => ch.Id.ToString());
        _reps.TryAdd("%channel.created%", () => ch.CreatedAt.ToString("HH:mm dd.MM.yyyy"));
        _reps.TryAdd("%channel.nsfw%", () => (ch as ITextChannel)?.IsNsfw.ToString() ?? "-");
        _reps.TryAdd("%channel.topic%", () => (ch as ITextChannel)?.Topic ?? "-");
        return this;
    }

    public ReplacementBuilder WithUser(IUser user)
    {
        WithManyUsers(new[] {user});
        return this;
    }

    public ReplacementBuilder WithManyUsers(IEnumerable<IUser> users)
    {
        /*OBSOLETE*/
        _reps.TryAdd("%user%", () => string.Join(" ", users.Select(user => user.Mention)));
        _reps.TryAdd("%userfull%", () => string.Join(" ", users.Select(user => user.ToString())));
        _reps.TryAdd("%username%", () => string.Join(" ", users.Select(user => user.Username)));
        _reps.TryAdd("%userdiscrim%", () => string.Join(" ", users.Select(user => user.Discriminator)));
        _reps.TryAdd("%useravatar%",
            () => string.Join(" ", users.Select(user => user.RealAvatarUrl()?.ToString())));
        _reps.TryAdd("%id%", () => string.Join(" ", users.Select(user => user.Id.ToString())));
        _reps.TryAdd("%uid%", () => string.Join(" ", users.Select(user => user.Id.ToString())));
        /*NEW*/
        _reps.TryAdd("%user.mention%", () => string.Join(" ", users.Select(user => user.Mention)));
        _reps.TryAdd("%user.fullname%", () => string.Join(" ", users.Select(user => user.ToString())));
        _reps.TryAdd("%user.name%", () => string.Join(" ", users.Select(user => user.Username)));
        _reps.TryAdd("%user.banner%",
            () => string.Join(" ",
                users.Select(async user => (await _client.Rest.GetUserAsync(user.Id)).GetBannerUrl(size: 2048))));
        _reps.TryAdd("%user.discrim%", () => string.Join(" ", users.Select(user => user.Discriminator)));
        _reps.TryAdd("%user.avatar%",
            () => string.Join(" ", users.Select(user => user.RealAvatarUrl()?.ToString())));
        _reps.TryAdd("%user.id%", () => string.Join(" ", users.Select(user => user.Id.ToString())));
        _reps.TryAdd("%user.created_time%",
            () => string.Join(" ", users.Select(user => user.CreatedAt.ToString("HH:mm"))));
        _reps.TryAdd("%user.created_date%",
            () => string.Join(" ", users.Select(user => user.CreatedAt.ToString("dd.MM.yyyy"))));
        _reps.TryAdd("%user.joined_time%",
            () => string.Join(" ", users.Select(user => (user as IGuildUser)?.JoinedAt?.ToString("HH:mm") ?? "-")));
        _reps.TryAdd("%user.joined_date%",
            () => string.Join(" ",
                users.Select(user => (user as IGuildUser)?.JoinedAt?.ToString("dd.MM.yyyy") ?? "-")));
        return this;
    }

    private ReplacementBuilder WithStats(DiscordSocketClient c)
    {
        /*OBSOLETE*/
        _reps.TryAdd("%servers%", () => c.Guilds.Count.ToString());
#if !GLOBAL_Mewdeko
        _reps.TryAdd("%users%", () => c.Guilds.Sum(s => s.Users.Count).ToString());
#endif

        /*NEW*/
        _reps.TryAdd("%shard.servercount%", () => c.Guilds.Count.ToString());
#if !GLOBAL_Mewdeko
        _reps.TryAdd("%shard.usercount%", () => c.Guilds.Sum(s => s.Users.Count).ToString());
#endif
        _reps.TryAdd("%shard.id%", () => c.ShardId.ToString());
        return this;
    }

    public ReplacementBuilder WithRngRegex()
    {
        var rng = new MewdekoRandom();
        _regex.TryAdd(_rngRegex, match =>
        {
            if (!int.TryParse(match.Groups["from"].ToString(), out var from))
                from = 0;
            if (!int.TryParse(match.Groups["to"].ToString(), out var to))
                to = 0;

            if (from == 0 && to == 0)
                return rng.Next(0, 11).ToString();

            return from >= to ? string.Empty : rng.Next(from, to + 1).ToString();
        });
        return this;
    }

    public ReplacementBuilder WithOverride(string key, Func<string> output)
    {
        _reps.AddOrUpdate(key, output, delegate { return output; });
        return this;
    }

    public Replacer Build() =>
        new(_reps.Select(x => (x.Key, x.Value)).ToArray(),
            _regex.Select(x => (x.Key, x.Value)).ToArray());

    public ReplacementBuilder WithProviders(IEnumerable<IPlaceholderProvider> phProviders)
    {
        foreach (var provider in phProviders)
        foreach (var ovr in provider.GetPlaceholders())
            _reps.TryAdd(ovr.Name, ovr.Func);

        return this;
    }
}