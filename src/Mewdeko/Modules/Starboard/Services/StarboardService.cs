using Discord;
using Discord.WebSocket;
using Mewdeko._Extensions;
using Mewdeko.Common.ModuleBehaviors;
using Mewdeko.Database;
using Mewdeko.Database.Extensions;
using Mewdeko.Database.Models;
using Serilog;

namespace Mewdeko.Modules.Starboard.Services;

public class StarboardService : INService, IReadyExecutor
{
    private readonly DiscordSocketClient _client;
    private readonly DbService _db;
    private readonly Mewdeko _bot;
    
    public StarboardService(DiscordSocketClient client, DbService db,
        Mewdeko bot)
    {
        _client = client;
        _db = db;
        _bot = bot;
        _client.ReactionAdded += OnReactionAddedAsync;
        _client.MessageDeleted += OnMessageDeletedAsync;
        _client.ReactionRemoved += OnReactionRemoveAsync;
        _client.ReactionsCleared += OnAllReactionsClearedAsync;
    }


    private List<StarboardPosts> starboardPosts;

    public Task OnReadyAsync() =>
        Task.FromResult(_ = Task.Run(() =>
        {
            using var uow = _db.GetDbContext();
            var all = uow.Starboard.All().ToList();
            starboardPosts = all.Any() ? all : new List<StarboardPosts>();
            Log.Information("Starboard Posts Cached.");
        }));

    private async Task AddStarboardPost(ulong messageId, ulong postId)
    {
        
        await using var uow = _db.GetDbContext();
        var post = starboardPosts.FirstOrDefault(x => x.MessageId == messageId);
        if (post is null)
        {
            var toAdd = new StarboardPosts {MessageId = messageId, PostId = postId};
            starboardPosts.Add(toAdd);
            uow.Starboard.Add(toAdd);
            await uow.SaveChangesAsync();
            return;
        }

        if (post.PostId == postId)
            return;
        
        starboardPosts.Remove(post);
        post.PostId = postId;
        uow.Starboard.Update(post);
        starboardPosts.Add(post);
        await uow.SaveChangesAsync();
    }

    private async Task RemoveStarboardPost(ulong messageId)
    {
        var toRemove = starboardPosts.FirstOrDefault(x => x.MessageId == messageId);
        await using var uow = _db.GetDbContext();
        uow.Starboard.Remove(toRemove);
        starboardPosts.Remove(toRemove);
        await uow.SaveChangesAsync();
    }
    
    public async Task SetStarboardChannel(IGuild guild, ulong channel)
    {
        await using (var uow = _db.GetDbContext())
        {
            var gc = uow.ForGuildId(guild.Id, set => set);
            gc.StarboardChannel = channel;
            await uow.SaveChangesAsync();
        }

        _bot.AllGuildConfigs[guild.Id].StarboardChannel = channel;
    }
    
    public async Task SetStarboardAllowBots(IGuild guild, bool enabled)
    {
        await using (var uow = _db.GetDbContext())
        {
            var gc = uow.ForGuildId(guild.Id, set => set);
            gc.StarboardAllowBots = enabled;
            await uow.SaveChangesAsync();
        }

        _bot.AllGuildConfigs[guild.Id].StarboardAllowBots = enabled;
    }
    
    public async Task SetRemoveOnDelete(IGuild guild, bool removeOnDelete)
    {
        await using (var uow = _db.GetDbContext())
        {
            var gc = uow.ForGuildId(guild.Id, set => set);
            gc.StarboardRemoveOnDelete = removeOnDelete;
            await uow.SaveChangesAsync();
        }

        _bot.AllGuildConfigs[guild.Id].StarboardRemoveOnDelete = removeOnDelete;
    }
    
    public async Task SetRemoveOnClear(IGuild guild, bool removeOnClear)
    {
        await using (var uow = _db.GetDbContext())
        {
            var gc = uow.ForGuildId(guild.Id, set => set);
            gc.StarboardRemoveOnReactionsClear = removeOnClear;
            await uow.SaveChangesAsync();
        }

        _bot.AllGuildConfigs[guild.Id].StarboardRemoveOnReactionsClear = removeOnClear;
    }
    
    public async Task SetRemoveOnBelowThreshold(IGuild guild, bool removeOnBelowThreshold)
    {
        await using (var uow = _db.GetDbContext())
        {
            var gc = uow.ForGuildId(guild.Id, set => set);
            gc.StarboardRemoveOnBelowThreshold = removeOnBelowThreshold;
            await uow.SaveChangesAsync();
        }

        _bot.AllGuildConfigs[guild.Id].StarboardRemoveOnBelowThreshold = removeOnBelowThreshold;
    }
    
    public async Task SetCheckMode(IGuild guild, bool checkmode)
    {
        await using (var uow = _db.GetDbContext())
        {
            var gc = uow.ForGuildId(guild.Id, set => set);
            gc.UseStarboardBlacklist = true;
            await uow.SaveChangesAsync();
        }

        _bot.AllGuildConfigs[guild.Id].UseStarboardBlacklist = checkmode;
    }
    public async Task<bool> ToggleChannel(IGuild guild, string id)
    {
        var channels = GetCheckedChannels(guild.Id).Split(" ").ToList();
        if (!channels.Contains(id))
        {
            channels.Add(id);
            var joinedchannels = string.Join(" ", channels);
            await using var uow = _db.GetDbContext();
            var gc = uow.ForGuildId(guild.Id, set => set);
            gc.StarboardCheckChannels = joinedchannels;
            await uow.SaveChangesAsync();
            _bot.AllGuildConfigs[guild.Id].StarboardCheckChannels = joinedchannels;
            return false;
        }

        channels.Remove(id);
        var joinedchannels1 = string.Join(" ", channels);
        await using var uow1 = _db.GetDbContext();
        var gc1 = uow1.ForGuildId(guild.Id, set => set);
        gc1.StarboardCheckChannels = joinedchannels1;
        await uow1.SaveChangesAsync();
        _bot.AllGuildConfigs[guild.Id].StarboardCheckChannels = joinedchannels1;
        return true;
    }

    public async Task SetStarCount(IGuild guild, int num)
    {
        await using (var uow = _db.GetDbContext())
        {
            var gc = uow.ForGuildId(guild.Id, set => set);
            gc.Stars = num;
            await uow.SaveChangesAsync();
        }

        _bot.AllGuildConfigs[guild.Id].Stars = num;
    }

    public int GetStarCount(ulong? id) => _bot.AllGuildConfigs[id.Value].Stars;
    public string GetCheckedChannels(ulong? id)
        => _bot.AllGuildConfigs[id.Value].StarboardCheckChannels;
    
    public bool GetAllowBots(ulong? id)
        => _bot.AllGuildConfigs[id.Value].StarboardAllowBots;

    public bool GetCheckMode(ulong? id)
        => _bot.AllGuildConfigs[id.Value].UseStarboardBlacklist;
    private int GetThreshold(ulong? id)
        => _bot.AllGuildConfigs[id.Value].RepostThreshold;
    
    private bool GetRemoveOnBelowThreshold(ulong? id)
        => _bot.AllGuildConfigs[id.Value].StarboardRemoveOnBelowThreshold;
    
    private bool GetRemoveOnDelete(ulong? id)
        => _bot.AllGuildConfigs[id.Value].StarboardRemoveOnDelete;
    
    private bool GetRemoveOnReactionsClear(ulong? id)
        => _bot.AllGuildConfigs[id.Value].StarboardRemoveOnReactionsClear;
    public async Task SetStar(IGuild guild, string emote)
    {
        await using (var uow = _db.GetDbContext())
        {
            var gc = uow.ForGuildId(guild.Id, set => set);
            gc.Star2 = emote;
            await uow.SaveChangesAsync();
        }

        _bot.AllGuildConfigs[guild.Id].Star2 = emote;
    }
    
    public async Task SetRepostThreshold(IGuild guild, int threshold)
    {
        await using (var uow = _db.GetDbContext())
        {
            var gc = uow.ForGuildId(guild.Id, set => set);
            gc.RepostThreshold = threshold;
            await uow.SaveChangesAsync();
        }

        _bot.AllGuildConfigs[guild.Id].RepostThreshold = threshold;
    }

    public string GetStar(ulong? id)
        => _bot.AllGuildConfigs[id.Value].Star2;

    private ulong GetStarboardChannel(ulong? id)
        => _bot.AllGuildConfigs[id.Value].StarboardChannel;

    private async Task OnReactionAddedAsync(Cacheable<IUserMessage, ulong> message,
        Cacheable<IMessageChannel, ulong> channel,
        SocketReaction reaction)
    {
        
        if (!reaction.User.IsSpecified
            || reaction.User.Value.IsBot 
            || !channel.HasValue 
            || channel.Value is not ITextChannel textChannel 
            || GetStarCount(textChannel.GuildId) == 0)
            return;
        
        var star = GetStar(textChannel.GuildId).ToIEmote();
        
        if (star.Name == null)
            return;
        
        if (!Equals(reaction.Emote, star))
            return;
        
        var starboardChannelSetting = GetStarboardChannel(textChannel.GuildId);
        
        if (starboardChannelSetting == 0)
            return;
        
        var starboardChannel = await textChannel.Guild.GetTextChannelAsync(starboardChannelSetting);
        
        if (starboardChannel == null)
            return;
        var gUser = await textChannel.Guild.GetUserAsync(_client.CurrentUser.Id);

        var botPerms = gUser.GetPermissions(starboardChannel);
        
        if (!botPerms.Has(ChannelPermission.SendMessages))
            return;
        string content;
        string imageurl;
        IUserMessage newMessage;
        if (!message.HasValue)
            newMessage = await message.GetOrDownloadAsync();
        else
            newMessage = message.Value;
        switch (newMessage.Author.IsBot)
        {
            case true when !GetAllowBots(textChannel.GuildId):
                return;
            case true:
                content = newMessage.Embeds.Any() ? newMessage.Embeds.Select(x => x.Description).FirstOrDefault() : newMessage.Content;
                imageurl = newMessage.Attachments.Any() ? newMessage.Attachments.FirstOrDefault().ProxyUrl : newMessage.Embeds?.Select(x => x.Image)?.FirstOrDefault()?.ProxyUrl;
                break;
            default:
                content = newMessage.Content;
                imageurl = newMessage.Attachments?.FirstOrDefault()?.ProxyUrl;
                break;
        }

        if (content is null && imageurl is null)
            return;
        
        var emoteCount = await newMessage.GetReactionUsersAsync(star, int.MaxValue).FlattenAsync();
        var count = emoteCount.Where(x => !x.IsBot);
        var enumerable = count as IUser[] ?? count.ToArray();
        if (enumerable.Length < GetStarCount(textChannel.GuildId))
            return;
        
        var maybePost = starboardPosts.FirstOrDefault(x => x.MessageId == newMessage.Id);
        if (maybePost != null)
        {
            if (GetThreshold(textChannel.GuildId) > 0)
            {
                var messages = await starboardChannel.GetMessagesAsync(GetThreshold(textChannel.GuildId)).FlattenAsync();
                var post = messages.FirstOrDefault(x => x.Id == maybePost.PostId);
                if (post is not null)
                {
                    var post2 = post as IUserMessage;
                    var eb1 = new EmbedBuilder().WithOkColor().WithAuthor(newMessage.Author)
                                               .WithDescription(content)
                                               .AddField("_ _", $"[Jump To Message]({newMessage.GetJumpUrl()})")
                                               .WithFooter(message.Id.ToString())
                                               .WithTimestamp(newMessage.Timestamp);
                    if (imageurl is not null)
                        eb1.WithImageUrl(imageurl);

                    await post2!.ModifyAsync(x =>
                    {
                        x.Content = $"{star} **{enumerable.Length}** {textChannel.Mention}";
                        x.Embed = eb1.Build();
                    });

                }
                else
                {
                    var tryGetOldPost = await starboardChannel.GetMessageAsync(maybePost.PostId);
                    if (tryGetOldPost is not null)
                        try
                        {
                            await tryGetOldPost.DeleteAsync();
                        }
                        catch 
                        {
                            // ignored
                        }
                    var eb2 = new EmbedBuilder().WithOkColor().WithAuthor(newMessage.Author)
                                               .WithDescription(content)
                                               .AddField("_ _", $"[Jump To Message]({newMessage.GetJumpUrl()})")
                                               .WithFooter(message.Id.ToString())
                                               .WithTimestamp(newMessage.Timestamp);
                    if (imageurl is not null)
                        eb2.WithImageUrl(imageurl);

                    var msg1 = await starboardChannel.SendMessageAsync($"{star} **{enumerable.Length}** {textChannel.Mention}", embed: eb2.Build());
                    await AddStarboardPost(message.Id, msg1.Id);

                }
            }
            else
            {
                var tryGetOldPost = await starboardChannel.GetMessageAsync(maybePost.PostId);
                if (tryGetOldPost is not null)
                {
                    var toModify = tryGetOldPost as IUserMessage;
                    var eb1 = new EmbedBuilder().WithOkColor().WithAuthor(newMessage.Author)
                                                .WithDescription(content)
                                                .AddField("_ _", $"[Jump To Message]({newMessage.GetJumpUrl()})")
                                                .WithFooter(message.Id.ToString())
                                                .WithTimestamp(newMessage.Timestamp);
                    if (imageurl is not null)
                        eb1.WithImageUrl(imageurl);

                    await toModify!.ModifyAsync(x =>
                    {
                        x.Content = $"{star} **{enumerable.Length}** {textChannel.Mention}";
                        x.Embed = eb1.Build();
                    });
                }
                else
                {
                    var eb2 = new EmbedBuilder().WithOkColor().WithAuthor(newMessage.Author)
                                               .WithDescription(content)
                                               .AddField("_ _", $"[Jump To Message]({newMessage.GetJumpUrl()})")
                                               .WithFooter(message.Id.ToString())
                                               .WithTimestamp(newMessage.Timestamp);
                    if (newMessage.Attachments.Any())
                        eb2.WithImageUrl(newMessage.Attachments.FirstOrDefault()!.Url);

                    var msg1 = await starboardChannel.SendMessageAsync($"{star} **{enumerable.Length}** {textChannel.Mention}", embed: eb2.Build());
                    await AddStarboardPost(message.Id, msg1.Id);
                }
            }
        }
        else
        {
            var eb = new EmbedBuilder().WithOkColor().WithAuthor(newMessage.Author)
                                       .WithDescription(content)
                                       .AddField("_ _", $"[Jump To Message]({newMessage.GetJumpUrl()})")
                                       .WithFooter(message.Id.ToString())
                                       .WithTimestamp(newMessage.Timestamp);
            if (imageurl is not null)
                eb.WithImageUrl(imageurl);

            var msg = await starboardChannel.SendMessageAsync($"{star} **{enumerable.Length}** {textChannel.Mention}", embed: eb.Build());
            await AddStarboardPost(message.Id, msg.Id);
        }

    }

    private async Task OnReactionRemoveAsync(Cacheable<IUserMessage, ulong> message,
        Cacheable<IMessageChannel, ulong> channel,
        SocketReaction reaction)
    {
        if (!reaction.User.IsSpecified
            || reaction.User.Value.IsBot 
            || !channel.HasValue 
            || channel.Value is not ITextChannel textChannel 
            || GetStarCount(textChannel.GuildId) == 0)
            return;

        var star = GetStar(textChannel.GuildId).ToIEmote();
        
        if (star.Name == null)
            return;
        
        if (!Equals(reaction.Emote, star))
            return;
        
        var starboardChannelSetting = GetStarboardChannel(textChannel.GuildId);
        
        if (starboardChannelSetting == 0)
            return;
        
        var starboardChannel = await textChannel.Guild.GetTextChannelAsync(starboardChannelSetting);

        if (starboardChannel == null)
            return;
        
        var gUser = await textChannel.Guild.GetUserAsync(_client.CurrentUser.Id);
        
        var botPerms = gUser.GetPermissions(starboardChannel);
        
        if (!botPerms.Has(ChannelPermission.SendMessages))
            return;
        
        string content;
        string imageurl;
        IUserMessage newMessage;
        if (!message.HasValue)
            newMessage = await message.GetOrDownloadAsync();
        else
            newMessage = message.Value;
        switch (newMessage.Author.IsBot)
        {
            case true when !GetAllowBots(textChannel.GuildId):
                return;
            case true:
                content = newMessage.Embeds.Any() ? newMessage.Embeds.Select(x => x.Description).FirstOrDefault() : newMessage.Content;
                imageurl = newMessage.Attachments.Any() ? newMessage.Attachments.FirstOrDefault().ProxyUrl : newMessage.Embeds?.Select(x => x.Image)?.FirstOrDefault()?.ProxyUrl;
                break;
            default:
                content = newMessage.Content;
                imageurl = newMessage.Attachments?.FirstOrDefault()?.ProxyUrl;
                break;
        }

        if (content is null && imageurl is null)
            return;

        var emoteCount = await newMessage.GetReactionUsersAsync(star, int.MaxValue).FlattenAsync();
        var maybePost = starboardPosts.FirstOrDefault(x => x.MessageId == newMessage.Id);
        if (maybePost == null)
            return;
        var count = emoteCount.Where(x => !x.IsBot);
        var enumerable = count as IUser[] ?? count.ToArray();
        if (enumerable.Length < GetStarCount(textChannel.GuildId) && GetRemoveOnBelowThreshold(gUser.GuildId))
        {
            await RemoveStarboardPost(newMessage.Id);
            try
            {
                var post = await starboardChannel.GetMessageAsync(maybePost.PostId);
                await post.DeleteAsync();
            }
            catch
            {
                // ignored
            }
        }
        else
        {
            if (GetThreshold(textChannel.GuildId) > 0)
            {
                var messages = await starboardChannel.GetMessagesAsync(GetThreshold(textChannel.GuildId)).FlattenAsync();
                var post = messages.FirstOrDefault(x => x.Id == maybePost.PostId);
                if (post is not null)
                {
                    var post2 = post as IUserMessage;
                    var eb1 = new EmbedBuilder().WithOkColor().WithAuthor(newMessage.Author)
                                               .WithDescription(content)
                                               .AddField("_ _", $"[Jump To Message]({newMessage.GetJumpUrl()})")
                                               .WithFooter(message.Id.ToString())
                                               .WithTimestamp(newMessage.Timestamp);
                    if (imageurl is not null)
                        eb1.WithImageUrl(imageurl);

                    await post2!.ModifyAsync(x =>
                    {
                        x.Content = $"{star} **{enumerable.Length}** {textChannel.Mention}";
                        x.Embed = eb1.Build();
                    });

                }
                else
                {
                    var tryGetOldPost = await starboardChannel.GetMessageAsync(maybePost.PostId);
                    if (tryGetOldPost is not null)
                        try
                        {
                            await tryGetOldPost.DeleteAsync();
                        }
                        catch 
                        {
                            // ignored
                        }
                    var eb2 = new EmbedBuilder().WithOkColor().WithAuthor(newMessage.Author)
                                               .WithDescription(content)
                                               .AddField("_ _", $"[Jump To Message]({newMessage.GetJumpUrl()})")
                                               .WithFooter(message.Id.ToString())
                                               .WithTimestamp(newMessage.Timestamp);
                    if (imageurl is not null)
                        eb2.WithImageUrl(imageurl);

                    var msg1 = await starboardChannel.SendMessageAsync($"{star} **{enumerable.Length}** {textChannel.Mention}", embed: eb2.Build());
                    await AddStarboardPost(newMessage.Id, msg1.Id);

                }
            }
            else
            {
                var tryGetOldPost = await starboardChannel.GetMessageAsync(maybePost.PostId);
                if (tryGetOldPost is not null)
                {
                    var toModify = tryGetOldPost as IUserMessage;
                    var eb1 = new EmbedBuilder().WithOkColor().WithAuthor(newMessage.Author)
                                                .WithDescription(content)
                                                .AddField("_ _", $"[Jump To Message]({newMessage.GetJumpUrl()})")
                                                .WithFooter(message.Id.ToString()).WithTimestamp(newMessage.Timestamp);
                    if (imageurl is not null)
                        eb1.WithImageUrl(imageurl);

                    await toModify!.ModifyAsync(x =>
                    {
                        x.Content = $"{star} **{enumerable.Length}** {textChannel.Mention}";
                        x.Embed = eb1.Build();
                    });
                }
                else
                {
                    var eb2 = new EmbedBuilder().WithOkColor().WithAuthor(newMessage.Author)
                                                .WithDescription(content)
                                                .AddField("_ _", $"[Jump To Message]({newMessage.GetJumpUrl()})")
                                                .WithFooter(message.Id.ToString()).WithTimestamp(newMessage.Timestamp);
                    if (imageurl is not null)
                        eb2.WithImageUrl(imageurl);

                    var msg1 = await starboardChannel.SendMessageAsync(
                        $"{star} **{enumerable.Length}** {textChannel.Mention}", embed: eb2.Build());
                    await AddStarboardPost(message.Id, msg1.Id);
                }
            }
        }
    }
    
    private async Task OnMessageDeletedAsync(Cacheable<IMessage, ulong> arg1, Cacheable<IMessageChannel, ulong> arg2)
    {
        if (!arg1.HasValue || !arg2.HasValue)
            return;

        var msg = arg1.Value;
        var chan = arg2.Value;
        if (chan is not ITextChannel channel)
            return;
        var permissions = (await channel.Guild.GetUserAsync(_client.CurrentUser.Id)).GetPermissions(channel);
        if (!permissions.ManageMessages)
            return;
        var maybePost = starboardPosts.FirstOrDefault(x => x.MessageId == msg.Id);
        if (maybePost is null)
            return;

        if (!GetRemoveOnDelete(channel.GuildId))
            return;

        var starboardChannel = await channel.Guild.GetTextChannelAsync(GetStarboardChannel(channel.GuildId));
        if (starboardChannel is null)
            return;

        var post = await starboardChannel.GetMessageAsync(maybePost.PostId);
        if (post is null)
            return;

        await starboardChannel.DeleteMessageAsync(post);
        await RemoveStarboardPost(msg.Id);
    }
    
    private async Task OnAllReactionsClearedAsync(Cacheable<IUserMessage, ulong> arg1, Cacheable<IMessageChannel, ulong> arg2)
    {
        IUserMessage msg;
        if (!arg1.HasValue)
            msg = await arg1.GetOrDownloadAsync();
        else
            msg = arg1.Value;
        
        if (msg is null)
            return;
        
        var maybePost = starboardPosts.FirstOrDefault(x => x.MessageId == msg.Id);
        
        if (maybePost is null || !arg2.HasValue || arg2.Value is not ITextChannel channel || !GetRemoveOnReactionsClear(channel.GuildId))
            return;
        
        var permissions = (await channel.Guild.GetUserAsync(_client.CurrentUser.Id)).GetPermissions(channel);
        if (!permissions.ManageMessages)
            return;

        var starboardChannel = await channel.Guild.GetTextChannelAsync(GetStarboardChannel(channel.GuildId));
        if (starboardChannel is null)
            return;

        await starboardChannel.DeleteMessageAsync(maybePost.PostId);
        await RemoveStarboardPost(msg.Id);
    }

    public StarboardPosts GetMessage(ulong id)
    {
        using var uow = _db.GetDbContext();
        return uow.Starboard.ForMsgId(id);
    }
}