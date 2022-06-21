﻿namespace Mewdeko.Database.Models;

public class StatusRoles : DbEntity
{
    public ulong GuildId { get; set; }
    public string Status { get; set; }
    public string ToAdd { get; set; }
    public string ToRemove { get; set; }
    public bool IsRegex { get; set; } = false;
}