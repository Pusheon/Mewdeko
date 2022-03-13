using System.Collections.Concurrent;
using Discord;
using Discord.WebSocket;
using Mewdeko._Extensions;
using Mewdeko.Common;
using Mewdeko.Common.Replacements;
using Mewdeko.Database;
using Mewdeko.Database.Extensions;
using Mewdeko.Database.Models;
using Mewdeko.Modules.Administration.Services;
using Mewdeko.Modules.Permissions.Common;
using Mewdeko.Modules.Permissions.Services;

namespace Mewdeko.Modules.Suggestions.Services;

public class SuggestionsService : INService
{
    public readonly DbService Db;
    private readonly PermissionService _perms;
    public DiscordSocketClient Client;
    public AdministrationService Adminserv;

    public CommandHandler CmdHandler;

    public SuggestionsService(
        DbService db,
        Mewdeko bot,
        CommandHandler cmd,
        DiscordSocketClient client,
        AdministrationService aserv,
        PermissionService permserv)
    {
        _perms = permserv;
        Adminserv = aserv;
        CmdHandler = cmd;
        Client = client;
        Client.MessageReceived += MessageRecieved;
        Db = db;
        Snum = bot.AllGuildConfigs.ToDictionary(x => x.GuildId, x => x.sugnum).ToConcurrent();
        Sugchans = bot.AllGuildConfigs.ToDictionary(x => x.GuildId, x => x.sugchan).ToConcurrent();
        Suggestmsgs = bot.AllGuildConfigs.ToDictionary(x => x.GuildId, x => x.SuggestMessage).ToConcurrent();
        Acceptmsgs = bot.AllGuildConfigs.ToDictionary(x => x.GuildId, x => x.AcceptMessage).ToConcurrent();
        Denymsgs = bot.AllGuildConfigs.ToDictionary(x => x.GuildId, x => x.DenyMessage).ToConcurrent();
        Implementmsgs = bot.AllGuildConfigs.ToDictionary(x => x.GuildId, x => x.ImplementMessage).ToConcurrent();
        Considermsgs = bot.AllGuildConfigs.ToDictionary(x => x.GuildId, x => x.ConsiderMessage).ToConcurrent();
        Suggestemotes = bot.AllGuildConfigs.ToDictionary(x => x.GuildId, x => x.SuggestEmotes).ToConcurrent();
        Minsuggestlengths = bot.AllGuildConfigs.ToDictionary(x => x.GuildId, x => x.MinSuggestLength).ToConcurrent();
        Maxsuggestlengths = bot.AllGuildConfigs.ToDictionary(x => x.GuildId, x => x.MaxSuggestLength).ToConcurrent();
    }

    private ConcurrentDictionary<ulong, string> Suggestemotes { get; }
    private ConcurrentDictionary<ulong, ulong> Sugchans { get; }
    private ConcurrentDictionary<ulong, ulong> Snum { get; }
    private ConcurrentDictionary<ulong, string> Suggestmsgs { get; }
    private ConcurrentDictionary<ulong, string> Acceptmsgs { get; }
    private ConcurrentDictionary<ulong, string> Denymsgs { get; }
    private ConcurrentDictionary<ulong, string> Implementmsgs { get; }
    private ConcurrentDictionary<ulong, string> Considermsgs { get; }
    private ConcurrentDictionary<ulong, int> Minsuggestlengths { get; }
    private ConcurrentDictionary<ulong, int> Maxsuggestlengths { get; }

    private async Task MessageRecieved(SocketMessage msg)
    {
        if (msg.Channel is not ITextChannel chan)
            return;
        var guild = (msg.Channel as IGuildChannel)?.Guild;
        var prefix = CmdHandler.GetPrefix(guild);
        if (guild != null
            && msg.Channel.Id == GetSuggestionChannel(guild.Id)
            && msg.Author.IsBot == false
            && !msg.Content.StartsWith(prefix))
        {
            if (msg.Channel.Id != GetSuggestionChannel(guild.Id))
                return;
            var guser = msg.Author as IGuildUser;
            var pc = _perms.GetCacheFor(guild.Id);
            var test = pc.Permissions.CheckPermissions(msg as IUserMessage, "suggest", "Suggestions".ToLowerInvariant(),
                out _);
            if (!test)
                return;
            if (guser.RoleIds.Contains(Adminserv.GetStaffRole(guser.Guild.Id)))
                return;
            if (msg.Content.Length > GetMaxLength(guild.Id))
            {
                try
                {
                    await msg.DeleteAsync();
                }
                catch
                {
                    // ignore
                }

                try
                {
                    await guser.SendErrorAsync(
                        $"Cannot send this suggestion as its over the max length `({GetMaxLength(guild.Id)})` of this server!");
                }
                catch
                {
                    // ignore
                }

                return;
            }

            if (msg.Content.Length < GetMinLength(guild.Id))
            {
                try
                {
                    await msg.DeleteAsync();
                }
                catch
                {
                    // ignore
                }

                try
                {
                    await guser.SendErrorAsync(
                        $"Cannot send this suggestion as its under the minimum length `({GetMaxLength(guild.Id)})` of this server!");
                }
                catch
                {
                    // ignore
                }

                return;
            }

            await SendSuggestion(chan.Guild, msg.Author as IGuildUser, Client, msg.Content,
                msg.Channel as ITextChannel);
            try
            {
                await msg.DeleteAsync();
            }
            catch
            {
                //ignored
            }
        }
    }

    private ulong GetSNum(ulong? id)
    {
        Snum.TryGetValue(id.Value, out var snum);
        return snum;
    }
    public int GetMaxLength(ulong? id)
    {
        Maxsuggestlengths.TryGetValue(id.Value, out var snum);
        return snum;
    }
    public int GetMinLength(ulong? id)
    {
        Minsuggestlengths.TryGetValue(id.Value, out var snum);
        return snum;
    }

    private string GetEmotes(ulong? id)
    {
        Suggestemotes.TryGetValue(id.Value, out var smotes);
        return smotes;
    }

    public async Task SetSuggestionEmotes(IGuild guild, string parsedEmotes)
    {
        await using (var uow = Db.GetDbContext())
        {
            var gc = uow.ForGuildId(guild.Id, set => set);
            gc.SuggestEmotes = parsedEmotes;
            await uow.SaveChangesAsync();
        }

        Suggestemotes.AddOrUpdate(guild.Id, parsedEmotes, (_, _) => parsedEmotes);
    }

    public async Task SetSuggestionChannelId(IGuild guild, ulong channel)
    {
        await using (var uow = Db.GetDbContext())
        {
            var gc = uow.ForGuildId(guild.Id, set => set);
            gc.sugchan = channel;
            await uow.SaveChangesAsync();
        }

        Sugchans.AddOrUpdate(guild.Id, channel, (_, _) => channel);
    }
    public async Task SetMinLength(IGuild guild, int minLength)
    {
        await using (var uow = Db.GetDbContext())
        {
            var gc = uow.ForGuildId(guild.Id, set => set);
            gc.MinSuggestLength = minLength;
            await uow.SaveChangesAsync();
        }

        Minsuggestlengths.AddOrUpdate(guild.Id, minLength, (_, _) => minLength);
    }
    public async Task SetMaxLength(IGuild guild, int maxLength)
    {
        await using (var uow = Db.GetDbContext())
        {
            var gc = uow.ForGuildId(guild.Id, set => set);
            gc.MaxSuggestLength = maxLength;
            await uow.SaveChangesAsync();
        }

        Maxsuggestlengths.AddOrUpdate(guild.Id, maxLength, (_, _) => maxLength);
    }


    public async Task SetSuggestionMessage(IGuild guild, string message)
    {
        await using (var uow = Db.GetDbContext())
        {
            var gc = uow.ForGuildId(guild.Id, set => set);
            gc.SuggestMessage = message;
            await uow.SaveChangesAsync();
        }

        Suggestmsgs.AddOrUpdate(guild.Id, message, (_, _) => message);
    }

    public async Task SetAcceptMessage(IGuild guild, string message)
    {
        await using (var uow = Db.GetDbContext())
        {
            var gc = uow.ForGuildId(guild.Id, set => set);
            gc.AcceptMessage = message;
            await uow.SaveChangesAsync();
        }

        Acceptmsgs.AddOrUpdate(guild.Id, message, (_, _) => message);
    }

    public async Task SetDenyMessage(IGuild guild, string message)
    {
        await using (var uow = Db.GetDbContext())
        {
            var gc = uow.ForGuildId(guild.Id, set => set);
            gc.DenyMessage = message;
            await uow.SaveChangesAsync();
        }

        Denymsgs.AddOrUpdate(guild.Id, message, (_, _) => message);
    }

    public async Task SetImplementMessage(IGuild guild, string message)
    {
        await using (var uow = Db.GetDbContext())
        {
            var gc = uow.ForGuildId(guild.Id, set => set);
            gc.ImplementMessage = message;
            await uow.SaveChangesAsync();
        }

        Implementmsgs.AddOrUpdate(guild.Id, message, (_, _) => message);
    }

    public async Task SetConsiderMessage(IGuild guild, string message)
    {
        await using (var uow = Db.GetDbContext())
        {
            var gc = uow.ForGuildId(guild.Id, set => set);
            gc.ConsiderMessage = message;
            await uow.SaveChangesAsync();
        }

        Considermsgs.AddOrUpdate(guild.Id, message, (_, _) => message);
    }

    public async Task Sugnum(IGuild guild, ulong num)
    {
        await using (var uow = Db.GetDbContext())
        {
            var gc = uow.ForGuildId(guild.Id, set => set);
            gc.sugnum = num;
            await uow.SaveChangesAsync();
        }

        Snum.AddOrUpdate(guild.Id, num, (_, _) => num);
    }

    public ulong GetSuggestionChannel(ulong? id)
    {
        if (id == null || !Sugchans.TryGetValue(id.Value, out var sugChan))
            return 0;

        return sugChan;
    }

    public string GetSuggestionMessage(IGuild guild)
    {
        Suggestmsgs.TryGetValue(guild.Id, out var snum);
        return snum == "" ? "" : snum;
    }

    public string GetAcceptMessage(IGuild guild)
    {
        Acceptmsgs.TryGetValue(guild.Id, out var snum);
        if (snum == "")
            return "";
        return snum;
    }

    public string GetDenyMessage(IGuild guild)
    {
        Denymsgs.TryGetValue(guild.Id, out var snum);
        if (snum == "")
            return "";
        return snum;
    }

    public string GetImplementMessage(IGuild guild)
    {
        Implementmsgs.TryGetValue(guild.Id, out var snum);
        if (snum == "")
            return "";
        return snum;
    }

    public string GetConsiderMessage(IGuild guild)
    {
        Considermsgs.TryGetValue(guild.Id, out var snum);
        if (snum == "")
            return "";
        return snum;
    }

    // public async Task DeleteUserSuggestions(Suggestionse[] suggestions, ITextChannel channel)
    // {
    //     foreach (var i in suggestions.Select(x => x.MessageID))
    //     {
    //         try
    //         {
    //             await channel.DeleteMessageAsync(i);
    //         }
    //         catch
    //         {
    //             //ignored
    //         }
    //     }
    //
    //     var uow = Db.GetDbContext();
    //     uow.Suggestions.RemoveRange(suggestions);
    //     await uow.SaveChangesAsync();
    // }
    public async Task SendDenyEmbed(IGuild guild, DiscordSocketClient client, IUser user, ulong suggestion,
        ITextChannel channel, string? reason = null, IDiscordInteraction? interaction = null)
    {
        string rs;
        rs = reason ?? "none";
        var suggest = Suggestions(guild.Id, suggestion).FirstOrDefault();
        var use = await guild.GetUserAsync(suggest.UserID);
        if (suggest.Suggestion is null)
        {
            if (interaction is null)
            {
                await channel.SendErrorAsync("That suggestion wasn't found! Please check the number and try again.");
                return;
            }

            await interaction.SendEphemeralErrorAsync("That suggestion wasn't found! Please check the number and try again.");
            return;
        }
        var eb = new EmbedBuilder();
        if (GetDenyMessage(guild) is "-" or "" or null)
        {
            if (suggest.Suggestion != null)
            {
                eb = new EmbedBuilder()
                    .WithAuthor(use)
                    .WithTitle($"Suggestion #{GetSNum(guild.Id) - 1} Denied")
                    .WithDescription(suggest.Suggestion)
                    .WithOkColor()
                    .AddField("Reason", rs);
            }
            else
            {
                var desc = await (await guild.GetTextChannelAsync(GetSuggestionChannel(guild.Id)))
                    .GetMessageAsync(suggest.MessageID);
                eb = new EmbedBuilder()
                    .WithAuthor(use)
                    .WithTitle($"Suggestion #{GetSNum(guild.Id) - 1} Denied")
                    .WithDescription(desc.Embeds.FirstOrDefault()?.Description)
                    .WithOkColor()
                    .AddField("Reason", rs);
            }

            var chan = await guild.GetTextChannelAsync(GetSuggestionChannel(guild.Id));
            var message = (await chan.GetMessageAsync(suggest.MessageID)) as IUserMessage;
            try
            {
                await message.RemoveAllReactionsAsync();
            }
            catch
            {
            }

            await message.ModifyAsync(x =>
            {
                x.Content = null;
                x.Embed = eb.Build();
            });
            try
            {
                var emb = new EmbedBuilder();
                emb.WithAuthor(use);
                emb.WithTitle($"Suggestion #{GetSNum(guild.Id) - 1} Denied");
                emb.WithDescription(suggest.Suggestion);
                emb.AddField("Reason", rs);
                emb.AddField("Denied By", user);
                emb.WithErrorColor();
                await (await guild.GetUserAsync(suggest.UserID)).SendMessageAsync(embed: emb.Build());
                if (interaction is null)
                    await channel.SendConfirmAsync("Suggestion set as denied and the user has been dmed.");
                else
                    await interaction.SendConfirmAsync("Suggestion set as denied and the user has been dmed.");
            }
            catch
            {
                if (interaction is null)
                    await channel.SendConfirmAsync("Suggestion set as denied but the user had their DMs off.");
                else
                    await interaction.SendConfirmAsync("Suggestion set as denied but the user had DMs off.");
            }
        }
        else
        {
            string sug;
            if (suggest.Suggestion == null)
                sug = (await (await guild.GetTextChannelAsync(GetSuggestionChannel(guild.Id)))
                    .GetMessageAsync(suggest.MessageID)).Embeds.FirstOrDefault()
                    ?.Description;
            else
                sug = suggest.Suggestion;
            var chan = await guild.GetTextChannelAsync(GetSuggestionChannel(guild.Id));
            var message = await chan.GetMessageAsync(suggest.MessageID) as IUserMessage;
            var suguse = await guild.GetUserAsync(suggest.UserID);
            var replacer = new ReplacementBuilder()
                .WithServer(client, guild as SocketGuild)
                .WithOverride("%suggest.user%", () => suguse.ToString())
                .WithOverride("%suggest.user.id%", () => suguse.Id.ToString())
                .WithOverride("%suggest.message%", () => sug)
                .WithOverride("%suggest.number%", () => suggest.SuggestID.ToString())
                .WithOverride("%suggest.user.name%", () => suguse.Username)
                .WithOverride("%suggest.user.avatar%", () => suguse.RealAvatarUrl().ToString())
                .WithOverride("%suggest.mod.user%", () => user.ToString())
                .WithOverride("%suggest.mod.avatar%", () => user.RealAvatarUrl().ToString())
                .WithOverride("%suggest.mod.name%", () => user.Username)
                .WithOverride("%suggest.mod.message%", () => rs)
                .WithOverride("%suggest.mod.Id%", () => user.Id.ToString())
                .Build();
            var ebe = SmartEmbed.TryParse(replacer.Replace(GetDenyMessage(guild)), out var embed, out var plainText);
            if (ebe is false)
            {
                if (interaction is null)
                    await channel.SendErrorAsync(
                        "The deny message is invalid, Please try again and notify a server admin about this. If you are having an issue please visit the support server shown when you mention Mewdeko.");
                if (interaction is not null)
                    await interaction.SendErrorAsync(
                        "The deny message is invalid, Please try again and notify a server admin about this. If you are having an issue please visit the support server shown when you mention Mewdeko.");
                await SetDenyMessage(guild, "-");
                return;
            }

            await message.ModifyAsync(x =>
            {
                x.Content = plainText;
                x.Embed = embed?.Build();
            });
            if (plainText is null && embed == null)
            {
                if (interaction is null)
                    await channel.SendErrorAsync(
                        "The deny message is invalid, Please try again and notify a server admin about this. If you are having an issue please visit the support server shown when you mention Mewdeko.");
                if (interaction is not null)
                    await interaction.SendErrorAsync(
                        "The deny message is invalid, Please try again and notify a server admin about this. If you are having an issue please visit the support server shown when you mention Mewdeko.");
                return;
            }

            try
            {
                var emb = new EmbedBuilder();
                emb.WithAuthor(use);
                emb.WithTitle($"Suggestion #{GetSNum(guild.Id) - 1} Denied");
                emb.WithDescription(suggest.Suggestion);
                emb.AddField("Reason", rs);
                emb.AddField("Denied By", user);
                emb.WithOkColor();
                await (await guild.GetUserAsync(suggest.UserID)).SendMessageAsync(embed: emb.Build());
                if (interaction is null)
                    await channel.SendConfirmAsync("Suggestion set as denied and the user has been dmed the denial!");
                else
                    await interaction.SendConfirmAsync("Suggestion set as denied and the user has been dmed.");
            }
            catch
            {
                if (interaction is null)
                    await channel.SendConfirmAsync("Suggestion set as denied but the user had their dms off.");
                else
                    await interaction.SendConfirmAsync("Suggestion set as denied but the user had DMs off.");
            }
        }
    }

    public async Task SendConsiderEmbed(IGuild guild, DiscordSocketClient client, IUser user, ulong suggestion,
        ITextChannel channel, string? reason = null, IDiscordInteraction? interaction = null)
    {
        string rs;
        if (reason == null)
            rs = "none";
        else
            rs = reason;
        var suggest = Suggestions(guild.Id, suggestion).FirstOrDefault();
        var use = await guild.GetUserAsync(suggest.UserID);
        if (suggest.Suggestion is null)
        {
            if (interaction is null)
            {
                await channel.SendErrorAsync("That suggestion wasn't found! Please check the number and try again.");
                return;
            }

            await interaction.SendEphemeralErrorAsync("That suggestion wasn't found! Please check the number and try again.");
            return;
        }

        var eb = new EmbedBuilder();
        if (GetConsiderMessage(guild) is "-" or "" or
            null)
        {
            if (suggest.Suggestion != null)
            {
                eb = new EmbedBuilder()
                    .WithAuthor(use)
                    .WithTitle($"Suggestion #{GetSNum(guild.Id) - 1} Considering")
                    .WithDescription(suggest.Suggestion)
                    .WithOkColor()
                    .AddField("Reason", rs);
            }
            else
            {
                var desc = await (await guild.GetTextChannelAsync(GetSuggestionChannel(guild.Id)))
                    .GetMessageAsync(suggest.MessageID);
                eb = new EmbedBuilder()
                    .WithAuthor(use)
                    .WithTitle($"Suggestion #{GetSNum(guild.Id) - 1} Considering")
                    .WithDescription(desc.Embeds.FirstOrDefault().Description)
                    .WithOkColor()
                    .AddField("Reason", rs);
            }

            var chan = await guild.GetTextChannelAsync(GetSuggestionChannel(guild.Id));
            var message = await chan.GetMessageAsync(suggest.MessageID) as IUserMessage;
            try
            {
                await message.RemoveAllReactionsAsync();
            }
            catch
            {
            }

            await message.ModifyAsync(x =>
            {
                x.Content = null;
                x.Embed = eb.Build();
            });
            try
            {
                var emb = new EmbedBuilder();
                emb.WithAuthor(use);
                emb.WithTitle($"Suggestion #{GetSNum(guild.Id) - 1} Considering");
                emb.WithDescription(suggest.Suggestion);
                emb.AddField("Reason", rs);
                emb.AddField("Denied By", user);
                emb.WithOkColor();
                await (await guild.GetUserAsync(suggest.UserID)).SendMessageAsync(embed: emb.Build());
                if (interaction is null)
                    await channel.SendConfirmAsync("Suggestion set as considered and the user has been dmed.");
                else
                    await interaction.SendConfirmAsync("Suggestion set as considered and the user has been dmed.");
            }
            catch
            {
                if (interaction is null)
                    await channel.SendConfirmAsync("Suggestion set as considered but the user had their dms off.");
                else
                    await interaction.SendConfirmAsync("Suggestion set as considered but the user had DMs off.");
            }
        }
        else
        {
            string sug;
            if (suggest.Suggestion == null)
                sug = (await (await guild.GetTextChannelAsync(GetSuggestionChannel(guild.Id)))
                           .GetMessageAsync(suggest.MessageID)).Embeds.FirstOrDefault()!.Description;
            else
                sug = suggest.Suggestion;
            var chan = await guild.GetTextChannelAsync(GetSuggestionChannel(guild.Id));
            var message = await chan.GetMessageAsync(suggest.MessageID) as IUserMessage;
            var suguse = await guild.GetUserAsync(suggest.UserID);
            var replacer = new ReplacementBuilder()
                .WithServer(client, guild as SocketGuild)
                .WithOverride("%suggest.user%", () => suguse.ToString())
                .WithOverride("%suggest.user.id%", () => suguse.Id.ToString())
                .WithOverride("%suggest.message%", () => sug)
                .WithOverride("%suggest.number%", () => suggest.SuggestID.ToString())
                .WithOverride("%suggest.user.name%", () => suguse.Username)
                .WithOverride("%suggest.user.avatar%", () => suguse.RealAvatarUrl().ToString())
                .WithOverride("%suggest.mod.user%", () => user.ToString())
                .WithOverride("%suggest.mod.avatar%", () => user.RealAvatarUrl().ToString())
                .WithOverride("%suggest.mod.name%", () => user.Username)
                .WithOverride("%suggest.mod.message%", () => rs)
                .WithOverride("%suggest.mod.Id%", () => user.Id.ToString())
                .Build();
            var ebe = SmartEmbed.TryParse(replacer.Replace(GetConsiderMessage(guild)), out var embed, out var plainText);
            if (ebe is false)
            {
                if (interaction is null)
                    await channel.SendErrorAsync(
                        "The consider message is invalid, Please try again and notify a server admin about this. If you are having an issue please visit the support server shown when you mention Mewdeko.");
                if (interaction is not null)
                    await interaction.SendErrorAsync(
                        "The consider message is invalid, Please try again and notify a server admin about this. If you are having an issue please visit the support server shown when you mention Mewdeko.");
                await SetConsiderMessage(guild, "-");
                return;
            }

            await message.ModifyAsync(x =>
            {
                x.Content = plainText;
                x.Embed = embed?.Build();
            });

            try
            {
                var emb = new EmbedBuilder();
                emb.WithAuthor(use);
                emb.WithTitle($"Suggestion #{GetSNum(guild.Id) - 1} Considering");
                emb.WithDescription(suggest.Suggestion);
                emb.AddField("Reason", rs);
                emb.AddField("Considered by", user);
                emb.WithOkColor();
                await (await guild.GetUserAsync(suggest.UserID)).SendMessageAsync(embed: emb.Build());
                if (interaction is null)
                    await channel.SendConfirmAsync("Suggestion set as considered and the user has been dmed.");
                else
                    await interaction.SendConfirmAsync("Suggestion set as considered and the user has been dmed.");
            }
            catch
            {
                if (interaction is null)
                    await channel.SendConfirmAsync("Suggestion set as considered but the user had their dms off.");
                else
                    await interaction.SendConfirmAsync("Suggestion set as considered but the user had DMs off.");
            }
        }
    }

    public async Task SendImplementEmbed(IGuild guild, DiscordSocketClient client, IUser user, ulong suggestion,
        ITextChannel channel, string? reason = null, IDiscordInteraction? interaction = null)
    {
        string rs;
        if (reason == null)
            rs = "none";
        else
            rs = reason;
        var suggest = Suggestions(guild.Id, suggestion).FirstOrDefault();
        var use = await guild.GetUserAsync(suggest.UserID);
        if (suggest.Suggestion is null)
        {
            if (interaction is null)
            {
                await channel.SendErrorAsync("That suggestion wasn't found! Please check the number and try again.");
                return;
            }

            await interaction.SendEphemeralErrorAsync("That suggestion wasn't found! Please check the number and try again.");
            return;
        }

        var eb = new EmbedBuilder();
        if (GetImplementMessage(guild) is "-" or "" or
            null)
        {
            if (suggest.Suggestion != null)
            {
                eb = new EmbedBuilder()
                    .WithAuthor(use)
                    .WithTitle($"Suggestion #{GetSNum(guild.Id) - 1} Implemented")
                    .WithDescription(suggest.Suggestion)
                    .WithOkColor()
                    .AddField("Reason", rs);
            }
            else
            {
                var desc = await (await guild.GetTextChannelAsync(GetSuggestionChannel(guild.Id)))
                    .GetMessageAsync(suggest.MessageID);
                eb = new EmbedBuilder()
                    .WithAuthor(use)
                    .WithTitle($"Suggestion #{GetSNum(guild.Id) - 1} Implemented")
                    .WithDescription(desc.Embeds.FirstOrDefault().Description)
                    .WithOkColor()
                    .AddField("Reason", rs);
            }

            var chan = await guild.GetTextChannelAsync(GetSuggestionChannel(guild.Id));
            var message = await chan.GetMessageAsync(suggest.MessageID) as IUserMessage;
            await message.ModifyAsync(x =>
            {
                x.Content = null;
                x.Embed = eb.Build();
            });
            try
            {
                await message.RemoveAllReactionsAsync();
            }
            catch
            {
            }

            try
            {
                var emb = new EmbedBuilder();
                emb.WithAuthor(use);
                emb.WithTitle($"Suggestion #{GetSNum(guild.Id) - 1} Implemented");
                emb.WithDescription(suggest.Suggestion);
                emb.AddField("Reason", rs);
                emb.AddField("Implemented By", user);
                emb.WithOkColor();
                await (await guild.GetUserAsync(suggest.UserID)).SendMessageAsync(embed: emb.Build());
                if (interaction is null)
                    await channel.SendConfirmAsync("Suggestion set as implemented and the user has been dmed.");
                else
                    await interaction.SendConfirmAsync("Suggestion set as implemented and the user has been dmed.");
            }
            catch
            {
                if (interaction is null)
                    await channel.SendConfirmAsync("Suggestion set as implemented but the user had their dms off.");
                else
                    await interaction.SendConfirmAsync("Suggestion set as implemented but the user had DMs off.");

            }
        }
        else
        {
            string sug;
            if (suggest.Suggestion == null)
                sug = (await (await guild.GetTextChannelAsync(GetSuggestionChannel(guild.Id)))
                    .GetMessageAsync(suggest.MessageID)).Embeds.FirstOrDefault()
                    ?.Description;
            else
                sug = suggest.Suggestion;
            var chan = await guild.GetTextChannelAsync(GetSuggestionChannel(guild.Id));
            var message = await chan.GetMessageAsync(suggest.MessageID) as IUserMessage;
            GetSNum(guild.Id);
            var suguse = await guild.GetUserAsync(suggest.UserID);
            var replacer = new ReplacementBuilder()
                .WithServer(client, guild as SocketGuild)
                .WithOverride("%suggest.user%", () => suguse.ToString())
                .WithOverride("%suggest.user.id%", () => suguse.Id.ToString())
                .WithOverride("%suggest.message%", () => sug)
                .WithOverride("%suggest.number%", () => suggest.SuggestID.ToString())
                .WithOverride("%suggest.user.name%", () => suguse.Username)
                .WithOverride("%suggest.user.avatar%", () => suguse.RealAvatarUrl().ToString())
                .WithOverride("%suggest.mod.user%", user.ToString)
                .WithOverride("%suggest.mod.avatar%", () => user.RealAvatarUrl().ToString())
                .WithOverride("%suggest.mod.name%", () => user.Username)
                .WithOverride("%suggest.mod.message%", () => rs)
                .WithOverride("%suggest.mod.Id%", () => user.Id.ToString())
                .Build();
            var ebe = SmartEmbed.TryParse(replacer.Replace(GetImplementMessage(guild)), out var embed, out var plainText);
            if (ebe is false)
            {
                await channel.SendErrorAsync(
                    "The implement message set is invalid, I have set it back to default to avoid further issues.  Please try again and notify a server admin about this. If you are having an issue please visit the suport server shown when you mention Mewdeko.");
                await SetImplementMessage(guild, "-");
                return;
            }

            await message.ModifyAsync(x =>
            {
                x.Content = plainText;
                x.Embed = embed?.Build();
            });

            try
            {
                var emb = new EmbedBuilder();
                emb.WithAuthor(use);
                emb.WithTitle($"Suggestion #{GetSNum(guild.Id) - 1} Implemented");
                emb.WithDescription(suggest.Suggestion);
                emb.AddField("Reason", rs);
                emb.AddField("Implemented By", user);
                emb.WithOkColor();
                await (await guild.GetUserAsync(suggest.UserID)).SendMessageAsync(embed: emb.Build());
                if (interaction is null)
                    await channel.SendConfirmAsync("Suggestion set as implemented and the user has been dmed.");
                else
                    await interaction.SendConfirmAsync("Suggestion set as implemented and the user has been dmed.");
            }
            catch
            {
                if (interaction is null)
                    await channel.SendConfirmAsync("Suggestion set as implemented but the user had their dms off.");
                else
                    await interaction.SendConfirmAsync("Suggestion set as implemented but the user had DMs off.");

            }
        }
    }

    public async Task SendAcceptEmbed(IGuild guild, DiscordSocketClient client, IUser user, ulong suggestion,
        ITextChannel channel, string? reason = null, IDiscordInteraction? interaction = null)
    {
        var rs = reason ?? "none";
        var suggest = Suggestions(guild.Id, suggestion).FirstOrDefault();
        var use = await guild.GetUserAsync(suggest.UserID);
        if (suggest.Suggestion is null)
        {
            if (interaction is null)
            {
                await channel.SendErrorAsync("That suggestion wasn't found! Please check the number and try again.");
                return;
            }

            await interaction.SendEphemeralErrorAsync("That suggestion wasn't found! Please check the number and try again.");
            return;
        }

        var eb = new EmbedBuilder();
        if (GetAcceptMessage(guild) is "-" or "" or null)
        {
            if (suggest.Suggestion != null)
            {
                eb = new EmbedBuilder()
                    .WithAuthor(use)
                    .WithTitle($"Suggestion #{GetSNum(guild.Id) - 1} Accepted")
                    .WithDescription(suggest.Suggestion)
                    .WithOkColor()
                    .AddField("Reason", rs);
            }
            else
            {
                var desc = await (await guild.GetTextChannelAsync(GetSuggestionChannel(guild.Id)))
                    .GetMessageAsync(suggest.MessageID);
                eb = new EmbedBuilder()
                    .WithAuthor(use)
                    .WithTitle($"Suggestion #{GetSNum(guild.Id) - 1} Accepted")
                    .WithDescription(desc.Embeds.FirstOrDefault().Description)
                    .WithOkColor()
                    .AddField("Reason", rs);
            }

            var chan = await guild.GetTextChannelAsync(GetSuggestionChannel(guild.Id));
            var message = await chan.GetMessageAsync(suggest.MessageID) as IUserMessage;
            await message.ModifyAsync(x =>
            {
                x.Content = null;
                x.Embed = eb.Build();
            });
            try
            {
                await message.RemoveAllReactionsAsync();
            }
            catch
            {
            }

            try
            {
                var emb = new EmbedBuilder();
                emb.WithAuthor(use);
                emb.WithTitle($"Suggestion #{GetSNum(guild.Id) - 1} Accepted");
                emb.WithDescription(suggest.Suggestion);
                emb.AddField("Reason", rs);
                emb.AddField("Accepted By", user);
                emb.WithOkColor();
                await (await guild.GetUserAsync(suggest.UserID)).SendMessageAsync(embed: emb.Build());
                if (interaction is null)
                    await channel.SendConfirmAsync("Suggestion set as accepted and the user has been dmed.");
                else
                    await interaction.SendConfirmAsync("Suggestion set as accepted and the user has been dmed.");
            }
            catch
            {
                if (interaction is null)
                    await channel.SendConfirmAsync("Suggestion set as accepted but the user had their dms off.");
                else
                    await interaction.SendConfirmAsync("Suggestion set as accepted but the user had DMs off.");

            }
        }
        else
        {
            string sug;
            if (suggest.Suggestion is null)
                sug = (await (await guild.GetTextChannelAsync(GetSuggestionChannel(guild.Id)))
                    .GetMessageAsync(suggest.MessageID)).Embeds.FirstOrDefault().Description;
            else
                sug = suggest.Suggestion;
            var chan = await guild.GetTextChannelAsync(GetSuggestionChannel(guild.Id));
            var message = await chan.GetMessageAsync(suggest.MessageID) as IUserMessage;
            GetSNum(guild.Id);
            var suguse = await guild.GetUserAsync(suggest.UserID);
            var replacer = new ReplacementBuilder()
                .WithServer(client, guild as SocketGuild)
                .WithOverride("%suggest.user%", () => suguse.ToString())
                .WithOverride("%suggest.user.id%", () => suguse.Id.ToString())
                .WithOverride("%suggest.message%", () => sug)
                .WithOverride("%suggest.number%", () => suggest.SuggestID.ToString())
                .WithOverride("%suggest.user.name%", () => suguse.Username)
                .WithOverride("%suggest.user.avatar%", () => suguse.RealAvatarUrl().ToString())
                .WithOverride("%suggest.mod.user%", () => user.ToString())
                .WithOverride("%suggest.mod.avatar%", () => user.RealAvatarUrl().ToString())
                .WithOverride("%suggest.mod.name%", () => user.Username)
                .WithOverride("%suggest.mod.message%", () => rs)
                .WithOverride("%suggest.mod.Id%", () => user.Id.ToString())
                .Build();
            var ebe = SmartEmbed.TryParse(replacer.Replace(GetAcceptMessage(guild)), out var embed, out var plainText);
            if (ebe is false)
            {
                await channel.SendErrorAsync(
                    "The accept message set is invalid, I have set it back to default to avoid further issues.  Please try again and notify a server admin about this. If you are having an issue please visit the suport server shown when you mention Mewdeko.");
                await SetAcceptMessage(guild, "-");
                return;
            }

            await message.ModifyAsync(x =>
            {
                x.Content = plainText;
                x.Embed = embed?.Build();
            });

            try
            {
                var emb = new EmbedBuilder();
                emb.WithAuthor(use);
                emb.WithTitle($"Suggestion #{GetSNum(guild.Id) - 1} Accepted");
                emb.WithDescription(suggest.Suggestion);
                emb.AddField("Reason", rs);
                emb.AddField("Accepted By", user);
                emb.WithOkColor();
                await (await guild.GetUserAsync(suggest.UserID)).SendMessageAsync(embed: emb.Build());
                if (interaction is null)
                    await channel.SendConfirmAsync("Suggestion set as accepted and the user has been dmed.");
                else
                    await interaction.SendConfirmAsync("Suggestion set as accepted and the user has been dmed.");
            }
            catch
            {
                if (interaction is null)
                    await channel.SendConfirmAsync("Suggestion set as accepted but the user had their dms off.");
                else
                    await interaction.SendConfirmAsync("Suggestion set as accepted but the user had DMs off.");
            }
        }
    }

    public async Task SendSuggestion(IGuild guild, IGuildUser user, DiscordSocketClient client, string suggestion,
        ITextChannel channel, IDiscordInteraction? interaction = null)
    {
        if (GetSuggestionChannel(guild.Id) == 0)
        {   
            if (interaction is null)
            {
                var msg = await channel.SendErrorAsync(
                "There is no suggestion channel set! Have an admin set it using `setsuggestchannel` and try again!");
                msg.DeleteAfter(3);
                return;
            }

            await interaction.SendEphemeralErrorAsync(
                "There is no suggestion channel set! Have an admin set it using `setsuggestchannel` then try again!");
            return;
        }

        var tup = new Emoji("\uD83D\uDC4D");
        var tdown = new Emoji("\uD83D\uDC4E");
        var emotes = new List<Emote>();
        var em = GetEmotes(guild.Id);
        if (em is not null and not "disable")
        {
            var te = em.Split(",");
            foreach (var emote in te) emotes.Add(Emote.Parse(emote));
        }

        if (GetSuggestionMessage(guild) is "-" or "")
        {
            var sugnum1 = GetSNum(guild.Id);
            var t = await (await guild.GetTextChannelAsync(GetSuggestionChannel(guild.Id))).EmbedAsync(
                new EmbedBuilder()
                    .WithAuthor(user)
                    .WithTitle($"Suggestion #{GetSNum(guild.Id)}")
                    .WithDescription(suggestion)
                    .WithOkColor());

            IEmote[] reacts = {tup, tdown};
            if (em is null or "disabled")
                foreach (var i in reacts)
                    await t.AddReactionAsync(i);
            else
                foreach (var ei in emotes)
                    await t.AddReactionAsync(ei);
            await Sugnum(guild, sugnum1 + 1);
            await Suggest(guild, sugnum1, t.Id, user.Id, suggestion);
            if (interaction is not null)
                await interaction.SendEphemeralConfirmAsync("Suggestion has been sent!");
        }
        else
        {
            var sugnum1 = GetSNum(guild.Id);
            var replacer = new ReplacementBuilder()
                .WithServer(client, guild as SocketGuild)
                .WithOverride("%suggest.user%", () => user.ToString())
                .WithOverride("%suggest.message%", () => suggestion)
                .WithOverride("%suggest.number%", () => sugnum1.ToString())
                .WithOverride("%suggest.user.name%", () => user.Username)
                .WithOverride("%suggest.user.avatar%", () => user.RealAvatarUrl().ToString())
                .Build();
            var ebe = SmartEmbed.TryParse(replacer.Replace(GetSuggestionMessage(guild)), out var embed, out var plainText);
            if (ebe is false)
            {
                if (interaction is null)
                {
                    await channel.SendErrorAsync(
                    "The custom suggest message set is invalid, I have set it back to default to avoid further issues. Please suggest again and notify a server admin about this. If you are having an issue please visit the support server shown when you mention Mewdeko.");
                    await SetSuggestionMessage(guild, "-");
                    return;
                }

                await interaction.SendEphemeralErrorAsync(
                    "The custom suggest message set is invalid, I have set it back to default to avoid further issues. Please suggest again and notify a server admin about this. If you are having an issue please visit the support server shown when you mention Mewdeko.");
                await SetSuggestionMessage(guild, "-");
                return;
            }

            
            var chan = await guild.GetTextChannelAsync(GetSuggestionChannel(guild.Id));
            var msg = await chan.SendMessageAsync(plainText,
                embed: embed?.Build());
            IEmote[] reacts = {tup, tdown};
            if (em is null or "disabled" or "-")
                foreach (var i in reacts)
                    await msg.AddReactionAsync(i);
            else
                foreach (var ei in emotes)
                    await msg.AddReactionAsync(ei);
            await Sugnum(guild, sugnum1 + 1);
            await Suggest(guild, sugnum1, msg.Id, user.Id, suggestion);

            if (interaction is not null)
                await interaction.SendEphemeralConfirmAsync("Suggestion has been sent!");
            else
                await channel.SendConfirmAsync("Suggestion sent!");
        }
    }

    public async Task Suggest(IGuild guild, ulong suggestId, ulong messageId, ulong userId, string suggestion)
    {
        var guildId = guild.Id;

        var suggest = new Suggestionse
        {
            GuildId = guildId,
            SuggestID = suggestId,
            MessageID = messageId,
            UserID = userId,
            Suggestion = suggestion
        };
        await using var uow = Db.GetDbContext();
        uow.Suggestions.Add(suggest);

        await uow.SaveChangesAsync();
    }

    public Suggestionse[] Suggestions(ulong gid, ulong sid)
    {
        using var uow = Db.GetDbContext();
        return uow.Suggestions.ForId(gid, sid);
    }

    public Suggestionse[] ForUser(ulong guildId, ulong userId)
    {
        using var uow = Db.GetDbContext();
        return uow.Suggestions.ForUser(guildId, userId);
    }
}