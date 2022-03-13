namespace Mewdeko.Database.Models;

public class FeedSub : DbEntity
{
    public int GuildConfigId { get; set; }
    public GuildConfig GuildConfig { get; set; }

    public ulong ChannelId { get; set; }
    public string Url { get; set; }
    public string Message { get; set; } = "-";

    public override int GetHashCode() => Url.GetHashCode(StringComparison.InvariantCulture) ^ GuildConfigId.GetHashCode();

    public override bool Equals(object obj) =>
        obj is FeedSub s
        && s.Url.ToLower() == Url.ToLower()
        && s.GuildConfigId == GuildConfigId;
}