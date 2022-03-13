using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Fergun.Interactive;
using Fergun.Interactive.Pagination;
using Humanizer;
using Humanizer.Localisation;
using Mewdeko._Extensions;
using Mewdeko.Common;
using Mewdeko.Common.Attributes;
using Mewdeko.Common.TypeReaders.Models;
using Mewdeko.Modules.Administration.Services;

namespace Mewdeko.Modules.Administration;

public partial class Administration : MewdekoModuleBase<AdministrationService>
{
    public enum Channel
    {
        Channel,
        Ch,
        Chnl,
        Chan
    }

    public enum List
    {
        List = 0,
        Ls = 0
    }

    public enum Server
    {
        Server
    }

    public enum State
    {
        Enable,
        Disable,
        Inherit
    }

    private readonly InteractiveService _interactivity;

    public Administration(InteractiveService serv) => _interactivity = serv;

    [MewdekoCommand, Usage, Description, Aliases, BotPerm(GuildPermission.ManageNicknames),
     UserPerm(GuildPermission.ManageNicknames), Priority(1)]
    public async Task SetNick(IGuildUser gu, [Remainder] string? newNick = null)
    {
        var sg = (SocketGuild) Context.Guild;
        if (sg.OwnerId == gu.Id ||
            gu.GetRoles().Max(r => r.Position) >= sg.CurrentUser.GetRoles().Max(r => r.Position))
        {
            await ReplyErrorLocalizedAsync("insuf_perms_i");
            return;
        }

        await gu.ModifyAsync(u => u.Nickname = newNick).ConfigureAwait(false);

        await ReplyConfirmLocalizedAsync("user_nick", Format.Bold(gu.ToString()), Format.Bold(newNick) ?? "-")
            .ConfigureAwait(false);
    }
    
    [MewdekoCommand, Usage, Description, Aliases, UserPerm(GuildPermission.ManageNicknames),
     BotPerm(GuildPermission.ChangeNickname), Priority(0)]
    public async Task SetNick([Remainder] string? newNick = null)
    {
        if (string.IsNullOrWhiteSpace(newNick))
            return;
        var curUser = await ctx.Guild.GetCurrentUserAsync().ConfigureAwait(false);
        await curUser.ModifyAsync(u => u.Nickname = newNick).ConfigureAwait(false);

        await ReplyConfirmLocalizedAsync("bot_nick", Format.Bold(newNick) ?? "-").ConfigureAwait(false);
    }
    [MewdekoCommand, Usage, Description, Aliases, UserPerm(GuildPermission.Administrator),
     BotPerm(GuildPermission.BanMembers)]
    public async Task BanUnder(StoopidTime time, string? option = null, StoopidTime? time1 = null)
    {
        await ctx.Guild.DownloadUsersAsync();
        IEnumerable<IUser> users = null;
        if (option is not null && option.ToLower() == "-accage" && time1 is not null)
            users = ((SocketGuild) ctx.Guild).Users.Where(c =>
                c.JoinedAt != null && DateTimeOffset.Now.Subtract(c.JoinedAt.Value).TotalSeconds <=
                time.Time.TotalSeconds &&
                DateTimeOffset.Now.Subtract(c.CreatedAt).TotalSeconds <= time1.Time.TotalSeconds);
        else
            users = ((SocketGuild) ctx.Guild).Users.Where(c =>
                c.JoinedAt != null && DateTimeOffset.Now.Subtract(c.JoinedAt.Value).TotalSeconds <=
                time.Time.TotalSeconds);

        if (!users.Any())
        {
            await ctx.Channel.SendErrorAsync("No users at or under that server join age!");
            return;
        }

        if (option is not null && option.ToLower() == "-p")
        {
            var paginator = new LazyPaginatorBuilder()
                .AddUser(ctx.User)
                .WithPageFactory(PageFactory)
                .WithFooter(PaginatorFooter.PageNumber | PaginatorFooter.Users)
                .WithMaxPageIndex(users.Count() - 1)
                .WithDefaultCanceledPage()
                .WithDefaultEmotes()
                .Build();
            await _interactivity.SendPaginatorAsync(paginator, Context.Channel, TimeSpan.FromMinutes(60));

            async Task<PageBuilder> PageFactory(int page)
            {
                await Task.CompletedTask;
                return new PageBuilder()
                       .WithTitle(
                           $"Previewing {users.Count()} users who's accounts joined under {time.Time.Humanize(maxUnit: TimeUnit.Year)} ago")
                       .WithDescription(string.Join("\n", users.Skip(page * 20).Take(20)));
            }
        }

        var banned = 0;
        var errored = 0;
        var embed = new EmbedBuilder().WithErrorColor()
            .WithDescription(
                $"Are you sure you want to ban {users.Count()} users that are under that server join age?");
        if (!await PromptUserConfirmAsync(embed, ctx.User.Id).ConfigureAwait(false)) return;
        var message = await ctx.Channel.SendConfirmAsync($"Banning {users.Count()} users..");
        foreach (var i in users)
            try
            {
                await ctx.Guild.AddBanAsync(i, reason: $"{ctx.User}|| Banning users under specified server join age.");
                banned++;
            }
            catch
            {
                errored++;
            }

        var eb = new EmbedBuilder()
            .WithDescription(
                $"Banned {banned} users under that server join age, and was unable to ban {errored} users.\nIf there were any failed bans please check the bots top role and try again.")
            .WithOkColor();
        await message.ModifyAsync(x => x.Embed = eb.Build());
    }

    [MewdekoCommand, Usage, Description, Aliases, UserPerm(GuildPermission.Administrator),
     BotPerm(GuildPermission.KickMembers)]
    public async Task KickUnder(StoopidTime time, string? option = null)
    {
        await ctx.Guild.DownloadUsersAsync();
        var users = ((SocketGuild) ctx.Guild).Users.Where(c =>
            c.JoinedAt != null && DateTimeOffset.Now.Subtract(c.JoinedAt.Value).TotalSeconds <= time.Time.TotalSeconds);
        var guildUsers = users as SocketGuildUser[] ?? users.ToArray();
        if (!guildUsers.Any())
        {
            await ctx.Channel.SendErrorAsync("No users at or under that account age!");
            return;
        }

        if (option is not null && option.ToLower() == "-p")
        {
            var paginator = new LazyPaginatorBuilder()
                .AddUser(ctx.User)
                .WithPageFactory(PageFactory)
                .WithFooter(PaginatorFooter.PageNumber | PaginatorFooter.Users)
                .WithMaxPageIndex(guildUsers.Length - 1)
                .WithDefaultCanceledPage()
                .WithDefaultEmotes()
                .Build();
            await _interactivity.SendPaginatorAsync(paginator, Context.Channel, TimeSpan.FromMinutes(60));

            async Task<PageBuilder> PageFactory(int page)
            {
                await Task.CompletedTask;
                return new PageBuilder()
                                       .WithTitle(
                                           $"Previewing {guildUsers.Length} users who's accounts joined under {time.Time.Humanize(maxUnit: TimeUnit.Year)} ago")
                                       .WithDescription(string.Join("\n", guildUsers.Skip(page * 20).Take(20)));
            }
        }

        var banned = 0;
        var errored = 0;
        var embed = new EmbedBuilder().WithErrorColor()
            .WithDescription($"Are you sure you want to kick {guildUsers.Length} users that joined under that time?");
        if (!await PromptUserConfirmAsync(embed, ctx.User.Id).ConfigureAwait(false)) return;
        var message = await ctx.Channel.SendConfirmAsync($"Kicking {guildUsers.Length} users..");
        foreach (var i in guildUsers)
            try
            {
                await i.KickAsync($"{ctx.User}|| Kicking users under specified join time.");
                banned++;
            }
            catch
            {
                errored++;
            }

        var eb = new EmbedBuilder()
            .WithDescription(
                $"Kicked {banned} users under that server join age, and was unable to ban {errored} users.\nIf there were any failed kicks please check the bots top role and try again.")
            .WithOkColor();
        await message.ModifyAsync(x => x.Embed = eb.Build());
    }


    [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild),
     UserPerm(GuildPermission.Administrator), BotPerm(GuildPermission.ManageGuild)]
    public async Task PruneMembers(StoopidTime time, string e = "no")
    {
        if (e == "no")
        {
            var toprune = await ctx.Guild.PruneUsersAsync(time.Time.Days, true);
            if (toprune == 0)
            {
                await ctx.Channel.SendErrorAsync(
                    $"No users to prune, if you meant to prune users inyour member role please set it with {Prefix}memberrole role, and rerun the command but specify -y after the time. You can also specify which roles you want to prune in by rerunning this with a role list at the end.");
                return;
            }

            var eb = new EmbedBuilder
            {
                Description = $"Are you sure you want to prune {toprune} Members?",
                Color = Mewdeko.OkColor
            };
            if (!await PromptUserConfirmAsync(eb, ctx.User.Id))
            {
                await ctx.Channel.SendConfirmAsync(
                    $"Canceled prune. As a reminder if you meant to prune members in your members role, set it with {Prefix}memberrole role and run this with -y at the end of the command. You can also specify which roles you want to prune in by rerunning this with a role list at the end.");
            }
            else
            {
                var msg = await ctx.Channel.SendConfirmAsync($"Pruning {toprune} members...");
                await ctx.Guild.PruneUsersAsync(time.Time.Days);
                var ebi = new EmbedBuilder
                {
                    Description = $"Pruned {toprune} members.",
                    Color = Mewdeko.OkColor
                };
                await msg.ModifyAsync(x => x.Embed = ebi.Build());
            }
        }
        else
        {
            var role = ctx.Guild.GetRole(Service.GetMemberRole(ctx.Guild.Id));
            var toprune = await ctx.Guild.PruneUsersAsync(time.Time.Days, true,
                includeRoleIds: new[] {Service.GetMemberRole(ctx.Guild.Id)});
            if (toprune == 0)
            {
                await ctx.Channel.SendErrorAsync("No users to prune.");
                return;
            }

            var eb = new EmbedBuilder
            {
                Description = $"Are you sure you want to prune {toprune} Members?",
                Color = Mewdeko.OkColor
            };
            if (!await PromptUserConfirmAsync(eb, ctx.User.Id))
            {
                await ctx.Channel.SendConfirmAsync("Canceled prune.");
            }
            else
            {
                var msg = await ctx.Channel.SendConfirmAsync($"Pruning {toprune} members...");
                await ctx.Guild.PruneUsersAsync(time.Time.Days,
                    includeRoleIds: new[] {Service.GetMemberRole(ctx.Guild.Id)});
                var ebi = new EmbedBuilder
                {
                    Description = $"Pruned {toprune} members.",
                    Color = Mewdeko.OkColor
                };
                await msg.ModifyAsync(x => x.Embed = ebi.Build());
            }
        }
    }

    [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild),
     UserPerm(GuildPermission.Administrator)]
    public async Task MemberRole(IRole? role)
    {
        var rol = Service.GetMemberRole(ctx.Guild.Id);
        if (rol is 0 && role != null)
        {
            await Service.MemberRoleSet(ctx.Guild, role.Id);
            await ctx.Channel.SendConfirmAsync($"Member role has been set to {role.Mention}");
        }

        if (rol != 0 && role != null && rol == role.Id)
        {
            await ctx.Channel.SendErrorAsync("This is already your Member role!");
            return;
        }

        if (rol is 0 && role == null)
        {
            await ctx.Channel.SendErrorAsync("No Member role set!");
            return;
        }

        if (rol != 0 && role is null)
        {
            var r = ctx.Guild.GetRole(rol);
            await ctx.Channel.SendConfirmAsync($"Your current Member role is {r.Mention}");
            return;
        }

        if (role != null && rol is not 0)
        {
            var oldrole = ctx.Guild.GetRole(rol);
            await Service.MemberRoleSet(ctx.Guild, role.Id);
            await ctx.Channel.SendConfirmAsync(
                $"Your Member role has been switched from {oldrole.Mention} to {role.Mention}");
        }
    }

    [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild),
     UserPerm(GuildPermission.Administrator)]
    public async Task StaffRole([Remainder] IRole? role = null)
    {
        var rol = Service.GetStaffRole(ctx.Guild.Id);
        if (rol is 0 && role != null)
        {
            await Service.StaffRoleSet(ctx.Guild, role.Id);
            await ctx.Channel.SendConfirmAsync($"Staff role has been set to {role.Mention}");
        }

        if (rol != 0 && role != null && rol == role.Id)
        {
            await ctx.Channel.SendErrorAsync("This is already your staff role!");
            return;
        }

        if (rol is 0 && role == null)
        {
            await ctx.Channel.SendErrorAsync("No staff role set!");
            return;
        }

        if (rol != 0 && role is null)
        {
            var r = ctx.Guild.GetRole(rol);
            await ctx.Channel.SendConfirmAsync($"Your current staff role is {r.Mention}");
            return;
        }

        if (role != null && rol is not 0)
        {
            var oldrole = ctx.Guild.GetRole(rol);
            await Service.StaffRoleSet(ctx.Guild, role.Id);
            await ctx.Channel.SendConfirmAsync(
                $"Your staff role has been switched from {oldrole.Mention} to {role.Mention}");
        }
    }

    [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild),
     UserPerm(GuildPermission.Administrator)]
    public async Task StaffRoleDisable()
    {
        var r = Service.GetStaffRole(ctx.Guild.Id);
        if (r == 0)
        {
            await ctx.Channel.SendErrorAsync("No staff role set!");
        }
        else
        {
            await Service.StaffRoleSet(ctx.Guild, 0);
            await ctx.Channel.SendConfirmAsync("Staff role disabled!");
        }
    }

    [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild),
     UserPerm(GuildPermission.Administrator), BotPerm(GuildPermission.ManageMessages), Priority(2)]
    public async Task Delmsgoncmd(List _)
    {
        var guild = (SocketGuild) ctx.Guild;
        var (enabled, channels) = Service.GetDelMsgOnCmdData(ctx.Guild.Id);

        var embed = new EmbedBuilder()
            .WithOkColor()
            .WithTitle(GetText("server_delmsgoncmd"))
            .WithDescription(enabled ? "✅" : "❌");

        var str = string.Join("\n", channels
            .Select(x =>
            {
                var ch = guild.GetChannel(x.ChannelId)?.ToString()
                         ?? x.ChannelId.ToString();
                var prefix = x.State ? "✅ " : "❌ ";
                return prefix + ch;
            }));

        if (string.IsNullOrWhiteSpace(str))
            str = "-";

        embed.AddField(GetText("channel_delmsgoncmd"), str);

        await ctx.Channel.EmbedAsync(embed).ConfigureAwait(false);
    }

    [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild),
     UserPerm(GuildPermission.Administrator), BotPerm(GuildPermission.ManageMessages), Priority(1)]
    public async Task Delmsgoncmd(Server _ = Server.Server)
    {
        if (Service.ToggleDeleteMessageOnCommand(ctx.Guild.Id))
        {
            Service.DeleteMessagesOnCommand.Add(ctx.Guild.Id);
            await ReplyConfirmLocalizedAsync("delmsg_on").ConfigureAwait(false);
        }
        else
        {
            Service.DeleteMessagesOnCommand.TryRemove(ctx.Guild.Id);
            await ReplyConfirmLocalizedAsync("delmsg_off").ConfigureAwait(false);
        }
    }

    [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild),
     UserPerm(GuildPermission.Administrator), BotPerm(GuildPermission.ManageMessages), Priority(0)]
    public Task Delmsgoncmd(Channel _, State s, ITextChannel ch) => Delmsgoncmd(_, s, ch.Id);

    [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild),
     UserPerm(GuildPermission.Administrator), BotPerm(GuildPermission.ManageMessages), Priority(1)]
    public async Task Delmsgoncmd(Channel _, State s, ulong? chId = null)
    {
        var actualChId = chId ?? ctx.Channel.Id;
        await Service.SetDelMsgOnCmdState(ctx.Guild.Id, actualChId, s).ConfigureAwait(false);

        switch (s)
        {
            case State.Disable:
                await ReplyConfirmLocalizedAsync("delmsg_channel_off").ConfigureAwait(false);
                break;
            case State.Enable:
                await ReplyConfirmLocalizedAsync("delmsg_channel_on").ConfigureAwait(false);
                break;
            default:
                await ReplyConfirmLocalizedAsync("delmsg_channel_inherit").ConfigureAwait(false);
                break;
        }
    }

    [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild),
     UserPerm(GuildPermission.DeafenMembers), BotPerm(GuildPermission.DeafenMembers)]
    public async Task Deafen(params IGuildUser[] users)
    {
        await AdministrationService.DeafenUsers(true, users).ConfigureAwait(false);
        await ReplyConfirmLocalizedAsync("deafen").ConfigureAwait(false);
    }

    [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild),
     UserPerm(GuildPermission.DeafenMembers), BotPerm(GuildPermission.DeafenMembers)]
    public async Task UnDeafen(params IGuildUser[] users)
    {
        await AdministrationService.DeafenUsers(false, users).ConfigureAwait(false);
        await ReplyConfirmLocalizedAsync("undeafen").ConfigureAwait(false);
    }

    [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild),
     UserPerm(GuildPermission.ManageChannels), BotPerm(GuildPermission.ManageChannels)]
    public async Task DelVoiChanl([Remainder] IVoiceChannel voiceChannel)
    {
        await voiceChannel.DeleteAsync().ConfigureAwait(false);
        await ReplyConfirmLocalizedAsync("delvoich", Format.Bold(voiceChannel.Name)).ConfigureAwait(false);
    }

    [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild),
     UserPerm(GuildPermission.ManageChannels), BotPerm(GuildPermission.ManageChannels)]
    public async Task CreatVoiChanl([Remainder] string channelName)
    {
        var ch = await ctx.Guild.CreateVoiceChannelAsync(channelName).ConfigureAwait(false);
        await ReplyConfirmLocalizedAsync("createvoich", Format.Bold(ch.Name)).ConfigureAwait(false);
    }

    [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild),
     UserPerm(GuildPermission.ManageChannels), BotPerm(GuildPermission.ManageChannels)]
    public async Task DelTxtChanl([Remainder] ITextChannel toDelete)
    {
        await toDelete.DeleteAsync().ConfigureAwait(false);
        await ReplyConfirmLocalizedAsync("deltextchan", Format.Bold(toDelete.Name)).ConfigureAwait(false);
    }

    [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild),
     UserPerm(GuildPermission.ManageChannels), BotPerm(GuildPermission.ManageChannels)]
    public async Task CreaTxtChanl([Remainder] string channelName)
    {
        var txtCh = await ctx.Guild.CreateTextChannelAsync(channelName).ConfigureAwait(false);
        await ReplyConfirmLocalizedAsync("createtextchan", Format.Bold(txtCh.Name)).ConfigureAwait(false);
    }

    [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild),
     UserPerm(GuildPermission.ManageChannels), BotPerm(GuildPermission.ManageChannels)]
    public async Task SetTopic([Remainder] string? topic = null)
    {
        var channel = (ITextChannel) ctx.Channel;
        topic ??= "";
        await channel.ModifyAsync(c => c.Topic = topic).ConfigureAwait(false);
        await ReplyConfirmLocalizedAsync("set_topic").ConfigureAwait(false);
    }

    [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild),
     UserPerm(GuildPermission.ManageChannels), BotPerm(GuildPermission.ManageChannels)]
    public async Task SetChanlName([Remainder] string name)
    {
        var channel = (ITextChannel) ctx.Channel;
        await channel.ModifyAsync(c => c.Name = name).ConfigureAwait(false);
        await ReplyConfirmLocalizedAsync("set_channel_name").ConfigureAwait(false);
    }

    [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild),
     UserPerm(GuildPermission.ManageChannels), BotPerm(GuildPermission.ManageChannels)]
    public async Task NsfwToggle()
    {
        var channel = (ITextChannel) ctx.Channel;
        var isEnabled = channel.IsNsfw;

        await channel.ModifyAsync(c => c.IsNsfw = !isEnabled).ConfigureAwait(false);

        if (isEnabled)
            await ReplyConfirmLocalizedAsync("nsfw_set_false").ConfigureAwait(false);
        else
            await ReplyConfirmLocalizedAsync("nsfw_set_true").ConfigureAwait(false);
    }

    [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild),
     UserPerm(ChannelPermission.ManageMessages), Priority(0)]
    public Task Edit(ulong messageId, [Remainder] string text) => Edit((ITextChannel) ctx.Channel, messageId, text);

    [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild), Priority(1)]
    public async Task Edit(ITextChannel channel, ulong messageId, [Remainder] string text)
    {
        var userPerms = ((SocketGuildUser) ctx.User).GetPermissions(channel);
        var botPerms = ((SocketGuild) ctx.Guild).CurrentUser.GetPermissions(channel);
        if (!userPerms.Has(ChannelPermission.ManageMessages))
        {
            await ReplyErrorLocalizedAsync("insuf_perms_u").ConfigureAwait(false);
            return;
        }

        if (!botPerms.Has(ChannelPermission.ViewChannel))
        {
            await ReplyErrorLocalizedAsync("insuf_perms_i").ConfigureAwait(false);
            return;
        }

        await AdministrationService.EditMessage(ctx, channel, messageId, text);
    }

    [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild),
     UserPerm(ChannelPermission.ManageMessages), BotPerm(ChannelPermission.ManageMessages)]
    public Task Delete(ulong messageId, StoopidTime? time = null) => Delete((ITextChannel) ctx.Channel, messageId, time);

    [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild)]
    public async Task Delete(ITextChannel channel, ulong messageId, StoopidTime? time = null) => await InternalMessageAction(channel, messageId, time);

    private async Task InternalMessageAction(ITextChannel channel, ulong messageId, StoopidTime? time)
    {
        var userPerms = ((SocketGuildUser) ctx.User).GetPermissions(channel);
        var botPerms = ((SocketGuild) ctx.Guild).CurrentUser.GetPermissions(channel);
        if (!userPerms.Has(ChannelPermission.ManageMessages))
        {
            await ReplyErrorLocalizedAsync("insuf_perms_u").ConfigureAwait(false);
            return;
        }

        if (!botPerms.Has(ChannelPermission.ManageMessages))
        {
            await ReplyErrorLocalizedAsync("insuf_perms_i").ConfigureAwait(false);
            return;
        }


        var msg = await channel.GetMessageAsync(messageId).ConfigureAwait(false);
        if (msg == null)
        {
            await ReplyErrorLocalizedAsync("msg_not_found").ConfigureAwait(false);
            return;
        }

        if (time == null)
        {
            await msg.DeleteAsync().ConfigureAwait(false);
        }
        else if (time.Time <= TimeSpan.FromDays(7))
        {
            var _ = Task.Run(async () =>
            {
                await Task.Delay(time.Time).ConfigureAwait(false);
                await msg.DeleteAsync().ConfigureAwait(false);
            });
        }
        else
        {
            await ReplyErrorLocalizedAsync("time_too_long").ConfigureAwait(false);
            return;
        }

        await ctx.OkAsync();
    }
}