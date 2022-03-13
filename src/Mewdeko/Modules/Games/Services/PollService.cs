using System.Collections.Concurrent;
using Discord;
using Mewdeko._Extensions;
using Mewdeko.Database;
using Mewdeko.Database.Common;
using Mewdeko.Database.Extensions;
using Mewdeko.Database.Models;
using Mewdeko.Modules.Games.Common;
using Serilog;

namespace Mewdeko.Modules.Games.Services;

public class PollService : INService
{
    private readonly DbService _db;

    public PollService(DbService db)
    {
        _db = db;

        using var uow = db.GetDbContext();
        ActivePolls = uow.Poll.GetAllPolls()
            .ToDictionary(x => x.GuildId, x =>
            {
                var pr = new PollRunner(db, x);
                return pr;
            })
            .ToConcurrent();
    }

    public ConcurrentDictionary<ulong, PollRunner> ActivePolls { get; }
    
    public async Task<(bool allowed, PollType type)> TryVote(IGuild guild, int num, IUser user)
    {

        if (!ActivePolls.TryGetValue(guild.Id, out var poll))
            return (false, PollType.PollEnded);

        try
        {
            return await poll.TryVote(num, user).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error voting");
        }

        return (true, poll.Poll.PollType);
    }

    public static Poll CreatePoll(ulong guildId, ulong channelId, string input, PollType type)
    {
        if (string.IsNullOrWhiteSpace(input) || !input.Contains(";"))
            return null;
        var data = input.Split(';');
        if (data.Length < 3 )
            return null;

        var col = new IndexedCollection<PollAnswer>(data.Skip(1)
                                                        .Select(x => new PollAnswer {Text = x}));

        return new Poll
        {
            Answers = col,
            Question = data[0],
            ChannelId = channelId,
            GuildId = guildId,
            Votes = new List<PollVote>(),
            PollType = type
        };
    }

    public bool StartPoll(Poll p)
    {
        var pr = new PollRunner(_db, p);
        if (!ActivePolls.TryAdd(p.GuildId, pr)) return false;
        using var uow = _db.GetDbContext();
        uow.Poll.Add(p);
        uow.SaveChanges();
        return true;

    }

    public Poll StopPoll(ulong guildId)
    {
        if (!ActivePolls.TryRemove(guildId, out var pr)) return null;
        using (var uow = _db.GetDbContext())
        {
            uow.RemovePoll(pr.Poll.Id);
            uow.SaveChanges();
        }

        return pr.Poll;

    }
    
}