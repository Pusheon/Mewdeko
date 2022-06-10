using Discord.Commands;
using Mewdeko.Common.Collections;
using Mewdeko.Services.strings;

namespace Mewdeko.Modules.Utility.Services;

public class VerboseErrorsService : INService, IUnloadableService
{
    private readonly CommandHandler _ch;
    private readonly DbService _db;
    private readonly IBotStrings _strings;
    private readonly ConcurrentHashSet<ulong> _guildsEnabled;

    public VerboseErrorsService(Mewdeko bot, DbService db, CommandHandler ch,
        IBotStrings strings)
    {
        _strings = strings;
        _db = db;
        _ch = ch;
        using var uow = db.GetDbContext();
        var gc = uow.GuildConfigs.All().Where(x => bot.GetCurrentGuildIds().Contains(x.GuildId));
        _ch.CommandErrored += LogVerboseError;

        _guildsEnabled = new ConcurrentHashSet<ulong>(gc
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
                .WithFooter($"Run {_ch.GetPrefix(channel.Guild?.Id)}ve to disable these prompts.")
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
            else gc.VerboseErrors = (bool)enabled; // New behaviour, just set it.

            uow.SaveChanges();
        }

        if ((bool)enabled) // This doesn't need to be duplicated inside the using block
            _guildsEnabled.Add(guildId);
        else
            _guildsEnabled.TryRemove(guildId);

        return (bool)enabled;
    }
}