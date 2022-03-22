using Discord;
using Discord.Commands;
using Mewdeko._Extensions;
using Mewdeko.Common.Collections;
using Mewdeko.Database;
using Mewdeko.Database.Extensions;
using Mewdeko.Modules.Help.Services;
using Mewdeko.Services.strings;

namespace Mewdeko.Modules.Utility.Services;

public class VerboseErrorsService : INService, IUnloadableService
{
    private readonly CommandHandler _ch;
    private readonly DbService _db;
    private readonly HelpService _hs;
    private readonly IBotStrings _strings;
    private readonly ConcurrentHashSet<ulong> _guildsEnabled;
    

    public VerboseErrorsService(Mewdeko bot, DbService db, CommandHandler ch, HelpService hs,
        IBotStrings strings)
    {
        _strings = strings;
        _db = db;
        _ch = ch;
        _hs = hs;

        _ch.CommandErrored += LogVerboseError;

        _guildsEnabled = new ConcurrentHashSet<ulong>(db.GetDbContext().GuildConfigs.All()
                                                        .Where(x => x.VerboseErrors)
            .Select(x => x.GuildId));
    }

    public Task Unload()
    {
        _ch.CommandErrored -= LogVerboseError;
        return Task.CompletedTask;
    }

    private async Task LogVerboseError(CommandInfo cmd, ITextChannel? channel, string reason)
    {
        if (channel == null || !_guildsEnabled.Contains(channel.GuildId))
            return;

        try
        {
            var embed = new EmbedBuilder()
                .WithTitle("Command Error")
                .WithDescription(reason)
                .AddField("Usages",
                    string.Join("\n", cmd.RealRemarksArr(_strings, channel.Guild.Id, _ch.GetPrefix(channel.Guild))))
                .WithErrorColor();
            
            await channel.SendMessageAsync(embed: embed.Build()).ConfigureAwait(false);
        }
        catch
        {
            //ignore
        }
    }

    public bool ToggleVerboseErrors(ulong guildId, bool? enabled = null)
    {
        using (var uow = _db.GetDbContext())
        {
            var gc = uow.ForGuildId(guildId, set => set);

            if (enabled == null)
                enabled = gc.VerboseErrors = !gc.VerboseErrors; // Old behaviour, now behind a condition
            else gc.VerboseErrors = (bool) enabled; // New behaviour, just set it.

            uow.SaveChanges();
        }

        if ((bool) enabled) // This doesn't need to be duplicated inside the using block
            _guildsEnabled.Add(guildId);
        else
            _guildsEnabled.TryRemove(guildId);

        return (bool) enabled;
    }
}