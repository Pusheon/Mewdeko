using LinqToDB;
using Mewdeko.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace Mewdeko.Database.Extensions;

public static class CustomReactionExtensions
{
    public static int ClearFromGuild(this DbSet<ChatTriggers> crs, ulong guildId) 
        => crs.Delete(x => x.GuildId == guildId);

    public static IEnumerable<ChatTriggers> ForId(this DbSet<ChatTriggers> crs, ulong id) =>
        crs
            .AsNoTracking()
            .AsQueryable()
            .Where(x => x.GuildId == id)
            .ToArray();

    public static ChatTriggers GetByGuildIdAndInput(this DbSet<ChatTriggers> crs, ulong? guildId, string input) 
        => crs.FirstOrDefault(x => x.GuildId == guildId && x.Trigger.ToUpper() == input);
}