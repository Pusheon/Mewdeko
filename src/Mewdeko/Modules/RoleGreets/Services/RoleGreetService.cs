using Discord;
using Discord.Webhook;
using Discord.WebSocket;
using Mewdeko._Extensions;
using Mewdeko.Common;
using Mewdeko.Common.Replacements;
using Mewdeko.Database;
using Mewdeko.Database.Extensions;
using Mewdeko.Database.Models;

namespace Mewdeko.Modules.RoleGreets.Services;

public class RoleGreetService : INService
{
    private readonly DbService _db;
    private readonly DiscordSocketClient _client;


    public RoleGreetService(DbService db, DiscordSocketClient client)
    {
        _client = client;
        _db = db;
        _client.GuildMemberUpdated += DoRoleGreet;
    }
    
    public RoleGreet[] GetGreets(ulong roleId) => _db.GetDbContext().RoleGreets.ForRoleId(roleId);

    public RoleGreet[] GetListGreets(ulong guildId) =>
        _db.GetDbContext().RoleGreets.Where(x => x.GuildId == guildId).ToArray();

    private Task DoRoleGreet(Cacheable<SocketGuildUser, ulong> cacheable, SocketGuildUser socketGuildUser)
    {
        _ = Task.Run(() =>
        {
            if (!cacheable.Value.Roles.SequenceEqual(socketGuildUser.Roles))
                if (cacheable.Value.Roles.Count > socketGuildUser.Roles.Count)
                    return;
            var diffRoles = socketGuildUser.Roles.Where(r => !cacheable.Value.Roles.Contains(r)).ToArray();
            foreach (var i in diffRoles)
            {
                var greets = GetGreets(i.Id);
                if (!greets.Any()) return;
                var webhooks = greets.Where(x => x.WebhookUrl is not null)
                                     .Select(x => new DiscordWebhookClient(x.WebhookUrl));
                if (greets.Any())
                {
                    async void Exec(SocketRole x) => await HandleChannelGreets(greets, x, socketGuildUser);

                    diffRoles.ForEach(Exec);
                }

                if (webhooks.Any())
                {
                    async void Exec(SocketRole x) => await HandleWebhookGreets(greets, x, socketGuildUser);

                    diffRoles.ForEach(Exec);
                }
            }
        });
        return Task.CompletedTask;

    }
    
    private async Task HandleChannelGreets(IEnumerable<RoleGreet> multiGreets, SocketRole role, SocketGuildUser user)
    {
        var checkGreets = multiGreets.Where(x => x.RoleId == role.Id);
        if (!checkGreets.Any())
            return;
        var replacer = new ReplacementBuilder().WithUser(user).WithClient(_client).WithServer(_client, user.Guild).Build();
        foreach (var i in checkGreets)
        {
            if (i.Disabled)
                continue;
            if (!i.GreetBots && user.IsBot)
                continue;
            if (i.WebhookUrl != null)
                continue;
            var channel = user.Guild.GetTextChannel(i.ChannelId);
            var content = replacer.Replace(i.Message);
            if (SmartEmbed.TryParse(content, out var embedData, out var plainText))
            {
                if (embedData is not null && plainText is not "")
                {
                    var msg = await channel.SendMessageAsync(plainText, embed: embedData.Build());
                    if (i.DeleteTime > 0)
                        msg.DeleteAfter(int.Parse(i.DeleteTime.ToString()));

                }

                if (embedData is null && plainText is not null)
                {
                    var msg = await channel.SendMessageAsync(plainText);
                    if (i.DeleteTime > 0)
                        msg.DeleteAfter(int.Parse(i.DeleteTime.ToString()));
                }

                if (embedData is not null && plainText is "")
                {
                    var msg = await channel.SendMessageAsync(embed: embedData.Build());
                    if (i.DeleteTime > 0)
                        msg.DeleteAfter(int.Parse(i.DeleteTime.ToString()));
                }
            }
            else
            {
                var msg = await channel.SendMessageAsync(content);
                if (i.DeleteTime > 0)
                    msg.DeleteAfter(int.Parse(i.DeleteTime.ToString()));
            }
        }
    }
    private async Task HandleWebhookGreets(IEnumerable<RoleGreet> multiGreets, SocketRole role, SocketGuildUser user)
    {
        var checkGreets = multiGreets.Where(x => x.RoleId == role.Id);
        if (!checkGreets.Any())
            return;
        var replacer = new ReplacementBuilder().WithUser(user).WithClient(_client).WithServer(_client, user.Guild).Build();
        foreach (var i in checkGreets)
        {
            if (i.WebhookUrl == null)
                continue;
            if (i.Disabled)
                continue;
            if (!i.GreetBots && user.IsBot)
                continue;
            if (i.WebhookUrl is null) continue;
            var webhook = new DiscordWebhookClient(i.WebhookUrl);
            var content = replacer.Replace(i.Message);
            if (SmartEmbed.TryParse(content, out var embedData, out var plainText))
            {
                if (embedData is not null && plainText is not "")
                {
                    var msg = await webhook.SendMessageAsync(plainText, embeds: new[] { embedData.Build() });
                    if (i.DeleteTime > 0)
                        (await user.Guild.GetTextChannel(i.ChannelId).GetMessageAsync(msg)).DeleteAfter(int.Parse(i.DeleteTime.ToString()));
                }

                if (embedData is null && plainText is not null)
                {
                    var msg = await webhook.SendMessageAsync(plainText);
                    if (i.DeleteTime > 0)
                        (await user.Guild.GetTextChannel(i.ChannelId).GetMessageAsync(msg)).DeleteAfter(int.Parse(i.DeleteTime.ToString()));
                }

                if (embedData is not null && plainText is "")
                {
                    var msg = await webhook.SendMessageAsync(embeds: new[] { embedData.Build() });
                    if (i.DeleteTime > 0)
                        (await user.Guild.GetTextChannel(i.ChannelId).GetMessageAsync(msg)).DeleteAfter(int.Parse(i.DeleteTime.ToString()));
                }
            }
            else
            {
                var msg = await webhook.SendMessageAsync(content);
                if (i.DeleteTime > 0)
                    (await user.Guild.GetTextChannel(i.ChannelId).GetMessageAsync(msg)).DeleteAfter(int.Parse(i.DeleteTime.ToString()));
            }
        }
    }
    
    
    public bool AddRoleGreet(ulong guildId, ulong channelId, ulong roleId)
    {
        if (GetGreets(guildId).Length == 10)
            return false;
        var toadd = new RoleGreet { ChannelId = channelId, GuildId = guildId, RoleId = roleId};
        var uow = _db.GetDbContext();
        uow.RoleGreets.Add(toadd);
        uow.SaveChangesAsync();
        return true;
    }

    public async Task ChangeMgMessage(RoleGreet greet, string code)
    {
        var uow = _db.GetDbContext();
        var toadd = new RoleGreet
        {
            Id = greet.Id,
            GuildId = greet.GuildId,
            RoleId = greet.RoleId,
            ChannelId = greet.ChannelId,
            DeleteTime = greet.DeleteTime,
            Message = code,
            GreetBots = greet.GreetBots,
            WebhookUrl = greet.WebhookUrl,
            Disabled = greet.Disabled
        };
        uow.RoleGreets.Update(toadd);
        await uow.SaveChangesAsync();
    }
    
    public async Task RoleGreetDisable(RoleGreet greet, bool disabled)
    {
        var uow = _db.GetDbContext();
        var toadd = new RoleGreet
        {
            Id = greet.Id,
            GuildId = greet.GuildId,
            RoleId = greet.RoleId,
            ChannelId = greet.ChannelId,
            DeleteTime = greet.DeleteTime,
            Message = greet.Message,
            GreetBots = greet.GreetBots,
            WebhookUrl = greet.WebhookUrl,
            Disabled = disabled
        };
        uow.RoleGreets.Update(toadd);
        await uow.SaveChangesAsync();
    }

    public async Task ChangeRgDelete(RoleGreet greet, ulong howlong)
    {
        var uow = _db.GetDbContext();
        var toadd = new RoleGreet
        {
            Id = greet.Id,
            GuildId = greet.GuildId,
            RoleId = greet.RoleId,
            ChannelId = greet.ChannelId,
            DeleteTime = howlong,
            Message = greet.Message,
            WebhookUrl = greet.WebhookUrl,
            Disabled = greet.Disabled,
            GreetBots = greet.GreetBots
        };
        uow.RoleGreets.Update(toadd);
        await uow.SaveChangesAsync();
    }
    public async Task ChangeMgWebhook(RoleGreet greet, string webhookurl)
    {
        var uow = _db.GetDbContext();
        var toadd = new RoleGreet
        {
            Id = greet.Id,
            GuildId = greet.GuildId,
            RoleId = greet.RoleId,
            ChannelId = greet.ChannelId,
            DeleteTime = greet.DeleteTime,
            Message = greet.Message,
            WebhookUrl = webhookurl,
            Disabled = greet.Disabled,
            GreetBots = greet.GreetBots
        };
        uow.RoleGreets.Update(toadd);
        await uow.SaveChangesAsync();
    }
    
    public async Task ChangeRgGb(RoleGreet greet, bool enabled)
    {
        var uow = _db.GetDbContext();
        var toadd = new RoleGreet
        {
            Id = greet.Id,
            GuildId = greet.GuildId,
            RoleId = greet.RoleId,
            ChannelId = greet.ChannelId,
            DeleteTime = greet.DeleteTime,
            Message = greet.Message,
            WebhookUrl = greet.WebhookUrl,
            Disabled = greet.Disabled,
            GreetBots = greet.GreetBots
        };
        uow.RoleGreets.Update(toadd);
        await uow.SaveChangesAsync();
    }

    public async Task RemoveRoleGreetInternal(RoleGreet greet)
    {
        var uow =  _db.GetDbContext();
        uow.RoleGreets.Remove(greet);
        await uow.SaveChangesAsync();
    }
    public async Task MultiRemoveRoleGreetInternal(RoleGreet[] greet)
    {
        var uow =  _db.GetDbContext();
        uow.RoleGreets.RemoveRange(greet);
        await uow.SaveChangesAsync();
    }
    
}