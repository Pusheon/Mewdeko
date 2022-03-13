namespace Mewdeko.Modules.Utility.Common;

public class SnipeStore
{
    public ulong GuildId { get; set; }
    public ulong UserId { get; set; }
    public ulong ChannelId { get; set; }
    public string Message { get; set; }
    public ulong Edited { get; set; }
    public DateTime DateAdded { get; set; }
}