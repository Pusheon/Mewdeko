using Mewdeko.Database.Models;

namespace Mewdeko.Modules.Chat_Triggers.Common;

public class ExportedTriggers
{
    public string[] React;
    public string Res { get; set; }
    public bool Ad { get; set; }
    public bool Dm { get; set; }
    public bool At { get; set; }
    public bool Ca { get; set; }
    public bool Rtt { get; set; }
    public bool Nr { get; set; }
    
    public static ExportedTriggers FromModel(Database.Models.ChatTriggers ct) =>
        new()
        {
            Res = ct.Response,
            Ad = ct.AutoDeleteTrigger,
            At = ct.AllowTarget,
            Ca = ct.ContainsAnywhere,
            Dm = ct.DmResponse,
            React = string.IsNullOrWhiteSpace(ct.Reactions)
                ? null
                : ct.GetReactions(),
            Rtt = ct.ReactToTrigger,
            Nr = ct.NoRespond
        };
}