using System.ComponentModel.DataAnnotations.Schema;
using System.Text.RegularExpressions;

namespace Mewdeko.Database.Models;

public class ChatTriggers : DbEntity
{
    [NotMapped] public Regex Regex { get; set; }

    public ulong UseCount { get; set; }
    public bool IsRegex { get; set; }
    public bool OwnerOnly { get; set; }

    public ulong? GuildId { get; set; }
    public string Response { get; set; }
    public string Trigger { get; set; }

    public bool AutoDeleteTrigger { get; set; }
    public bool ReactToTrigger { get; set; }
    public bool NoRespond { get; set; }
    public bool DmResponse { get; set; }
    public bool ContainsAnywhere { get; set; }
    public bool AllowTarget { get; set; }
    public string Reactions { get; set; }

    public string[] GetReactions() =>
        string.IsNullOrWhiteSpace(Reactions)
            ? Array.Empty<string>()
            : Reactions.Split("@@@");


    public bool IsGlobal() => GuildId is null or 0;
}

public class ReactionResponse : DbEntity
{
    public bool OwnerOnly { get; set; }
    public string Text { get; set; }
}