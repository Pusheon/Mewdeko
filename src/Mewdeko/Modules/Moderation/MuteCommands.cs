using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Humanizer;
using Mewdeko._Extensions;
using Mewdeko.Common;
using Mewdeko.Common.Attributes;
using Mewdeko.Common.TypeReaders.Models;
using Mewdeko.Modules.Moderation.Services;
using Serilog;
using PermValue = Discord.PermValue;

namespace Mewdeko.Modules.Moderation;

public partial class Moderation
{
    [Group]
    public class MuteCommands : MewdekoSubmodule<MuteService>
    {
        private async Task<bool> VerifyMutePermissions(IGuildUser runnerUser, IGuildUser targetUser)
        {
            var runnerUserRoles = runnerUser.GetRoles();
            var targetUserRoles = targetUser.GetRoles();
            if (runnerUser.Id == ctx.Guild.OwnerId ||
                runnerUserRoles.Max(x => x.Position) > targetUserRoles.Max(x => x.Position)) return true;
            await ReplyErrorLocalizedAsync("mute_perms").ConfigureAwait(false);
            return false;
        }

        [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild),
         UserPerm(GuildPermission.MuteMembers), Priority(1)]
        public async Task Stfu(StoopidTime time, IUser user) => await Stfu(user, time);

        [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild),
         UserPerm(GuildPermission.Administrator)]
        public async Task RemoveOnMute(string yesnt)
        {
            var users = (await ctx.Guild.GetUsersAsync()).Where(x => x.RoleIds.ToList().Contains(824321516468043779));
            if (yesnt.StartsWith("n"))
            {
                await Service.Removeonmute(ctx.Guild, "n");
                await ctx.Channel.SendConfirmAsync("Removing roles on mute has been disabled!");
            }

            if (yesnt.StartsWith("y"))
            {
                await Service.Removeonmute(ctx.Guild, "y");
                await ctx.Channel.SendConfirmAsync("Removing roles on mute has been enabled!");
            }
            else
            {
                await ctx.Channel.SendErrorAsync("Hey! Its either yes or no, Not that I care anyway, hmph.");
            }
        }

        [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild),
         UserPerm(GuildPermission.MuteMembers), Priority(0)]
        public async Task Stfu(IUser user, StoopidTime? time = null)
        {
            var channel = ctx.Channel as SocketGuildChannel;
            var currentPerms = channel.GetPermissionOverwrite(user) ?? new OverwritePermissions();
            await channel.AddPermissionOverwriteAsync(user, currentPerms.Modify(sendMessages: PermValue.Deny));
            if (time is null)
                await ctx.Channel.SendConfirmAsync($"{user} has been muted in this channel!");
            if (time != null)
            {
                await channel.AddPermissionOverwriteAsync(user, currentPerms.Modify(sendMessages: PermValue.Deny));
                await ctx.Channel.SendConfirmAsync(
                    $"{user} has been muted in this channel for {time.Time.Humanize()}!");
                await Task.Delay((int) time.Time.TotalMilliseconds);
                try
                {
                    await channel.AddPermissionOverwriteAsync(user,
                        currentPerms.Modify(sendMessages: PermValue.Inherit));
                }
                catch
                {
                }
            }
        }

        [MewdekoCommand, Aliases, Description, RequireContext(ContextType.Guild),
         UserPerm(GuildPermission.Administrator)]
        public async Task UnmuteAll([Remainder] string? reason = null)
        {
            var users = (await ctx.Guild.GetUsersAsync())
                           .Where(x => x.RoleIds.ToList().Contains((Service.GetMuteRole(ctx.Guild).Result.Id)));
            if (!users.Any())
            {
                await ctx.Channel.SendErrorAsync("There are no muted users or you don't have a mute role set.");
                return;
            }
            if (await PromptUserConfirmAsync(
                    new EmbedBuilder().WithOkColor().WithDescription(
                        "Are you absolutely sure you want to unmute ***all*** users? This action is irreversible."),
                    ctx.User.Id))
            {
                if (reason is null)
                {
                    if (await PromptUserConfirmAsync(new EmbedBuilder().WithOkColor().WithDescription("Would you like to add a reason for the unmute?"), ctx.User.Id))
                    {
                        var msg = await ctx.Channel.SendMessageAsync("Please type out the unmute reason.");
                        reason = await NextMessageAsync(ctx.Channel.Id, ctx.User.Id);
                        var eb = new EmbedBuilder().WithDescription($"Unmuting {users.Count()} users...");
                        await msg.ModifyAsync(x => x.Embed = eb.Build());
                        foreach (var i in users)
                        {
                            try
                            {
                                await Service.UnmuteUser(i.GuildId, i.Id, ctx.User, MuteType.All, reason);
                            }
                            catch
                            {
                               // ignored
                            }                          
                        }

                        await msg.ModifyAsync(x =>
                            x.Embed = new EmbedBuilder().WithOkColor().WithDescription("Unmuted all users!").Build());
                    }
                    else
                    {
                        var eb = new EmbedBuilder().WithDescription($"Unmuting {users.Count()} users...");
                        var msg = await ctx.Channel.SendMessageAsync(embed: eb.Build());
                        foreach (var i in users)
                        {
                            try
                            {
                                await Service.UnmuteUser(i.GuildId, i.Id, ctx.User, MuteType.All);
                            }
                            catch
                            {
                                // ignored
                            }                          
                        }

                        await msg.ModifyAsync(x =>
                            x.Embed = new EmbedBuilder().WithOkColor().WithDescription("Unmuted all users!").Build());
                    }
                }
                else
                {
                    var eb = new EmbedBuilder().WithDescription($"Unmuting {users.Count()} users...");
                    var msg = await ctx.Channel.SendMessageAsync(embed: eb.Build());
                    foreach (var i in users)
                    {
                        try
                        {
                            await Service.UnmuteUser(i.GuildId, i.Id, ctx.User, MuteType.All, reason);
                        }
                        catch
                        {
                            // ignored
                        }                          
                    }

                    await msg.ModifyAsync(x =>
                        x.Embed = new EmbedBuilder().WithOkColor().WithDescription("Unmuted all users!").Build());
                }
            }
        }

        [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild),
         UserPerm(GuildPermission.MuteMembers)]
        public async Task Unstfu(IUser user)
        {
            var channel = ctx.Channel as SocketGuildChannel;
            var currentPerms = channel.GetPermissionOverwrite(user) ?? new OverwritePermissions();
            await channel.AddPermissionOverwriteAsync(user, currentPerms.Modify(sendMessages: PermValue.Inherit));
            await ctx.Channel.SendConfirmAsync($"{user} has been unmuted in this channel!");
        }

        [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild),
         UserPerm(GuildPermission.ManageRoles)]
#pragma warning disable 108,114
        public async Task MuteRole([Remainder] IRole? role = null)
#pragma warning restore 108,114
        {
            if (role is null)
            {
                var muteRole = await Service.GetMuteRole(ctx.Guild).ConfigureAwait(false);
                await ReplyConfirmLocalizedAsync("mute_role", muteRole.Mention).ConfigureAwait(false);
                return;
            }

            if (Context.User.Id != Context.Guild.OwnerId &&
                role.Position >= ((SocketGuildUser) Context.User).Roles.Max(x => x.Position))
            {
                await ReplyErrorLocalizedAsync("insuf_perms_u").ConfigureAwait(false);
                return;
            }

            await Service.SetMuteRoleAsync(ctx.Guild.Id, role.Name).ConfigureAwait(false);

            await ReplyConfirmLocalizedAsync("mute_role_set").ConfigureAwait(false);
        }

        [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild),
         UserPerm(GuildPermission.ManageRoles | GuildPermission.MuteMembers), Priority(0)]
        public async Task Mute(IGuildUser target, [Remainder] string reason = "")
        {
            try
            {
                if (!await VerifyMutePermissions((IGuildUser) ctx.User, target))
                    return;

                await Service.MuteUser(target, ctx.User, reason: reason).ConfigureAwait(false);
                await ReplyConfirmLocalizedAsync("user_muted", Format.Bold(target.ToString()))
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Warning(ex.ToString());
                await ReplyErrorLocalizedAsync("mute_error").ConfigureAwait(false);
            }
        }

        [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild),
         UserPerm(GuildPermission.ManageRoles | GuildPermission.MuteMembers), Priority(2)]
        public async Task Mute(IGuildUser user, StoopidTime time, string reason = "") => await Mute(time, user, reason);

        [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild),
         UserPerm(GuildPermission.ManageRoles | GuildPermission.MuteMembers), Priority(1)]
        public async Task Mute(StoopidTime time, IGuildUser user, [Remainder] string reason = "")
        {
            if (time.Time < TimeSpan.FromMinutes(1) || time.Time > TimeSpan.FromDays(90))
                return;
            try
            {
                if (!await VerifyMutePermissions((IGuildUser) ctx.User, user))
                    return;

                await Service.TimedMute(user, ctx.User, time.Time, reason: reason).ConfigureAwait(false);
                await ReplyConfirmLocalizedAsync("user_muted_time", Format.Bold(user.ToString()),
                    time.Time.Humanize()).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error in mute command");
                await ReplyErrorLocalizedAsync("mute_error").ConfigureAwait(false);
            }
        }

        [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild),
         UserPerm(GuildPermission.ManageRoles | GuildPermission.MuteMembers)]
        public async Task Unmute(IGuildUser user, [Remainder] string reason = "")
        {
            try
            {
                await Service.UnmuteUser(user.GuildId, user.Id, ctx.User, reason: reason).ConfigureAwait(false);
                await ReplyConfirmLocalizedAsync("user_unmuted", Format.Bold(user.ToString()))
                    .ConfigureAwait(false);
            }
            catch
            {
                await ReplyErrorLocalizedAsync("mute_error").ConfigureAwait(false);
            }
        }

        [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild),
         UserPerm(GuildPermission.ManageRoles), Priority(0)]
        public async Task ChatMute(IGuildUser user, [Remainder] string reason = "")
        {
            try
            {
                if (!await VerifyMutePermissions((IGuildUser) ctx.User, user))
                    return;

                await Service.MuteUser(user, ctx.User, MuteType.Chat, reason).ConfigureAwait(false);
                await ReplyConfirmLocalizedAsync("user_chat_mute", Format.Bold(user.ToString()))
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Warning(ex.ToString());
                await ReplyErrorLocalizedAsync("mute_error").ConfigureAwait(false);
            }
        }

        [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild),
         UserPerm(GuildPermission.ManageRoles)]
        public async Task ChatUnmute(IGuildUser user, [Remainder] string reason = "")
        {
            try
            {
                await Service.UnmuteUser(user.Guild.Id, user.Id, ctx.User, MuteType.Chat, reason)
                    .ConfigureAwait(false);
                await ReplyConfirmLocalizedAsync("user_chat_unmute", Format.Bold(user.ToString()))
                    .ConfigureAwait(false);
            }
            catch
            {
                await ReplyErrorLocalizedAsync("mute_error").ConfigureAwait(false);
            }
        }

        [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild),
         UserPerm(GuildPermission.MuteMembers), Priority(1)]
        public async Task VoiceMute(StoopidTime time, IGuildUser user, [Remainder] string reason = "")
        {
            if (time.Time < TimeSpan.FromMinutes(1) || time.Time > TimeSpan.FromDays(49))
                return;
            try
            {
                if (!await VerifyMutePermissions((IGuildUser) ctx.User, user))
                    return;

                await Service.TimedMute(user, ctx.User, time.Time, MuteType.Voice, reason).ConfigureAwait(false);
                await ReplyConfirmLocalizedAsync("user_voice_mute_time", Format.Bold(user.ToString()),
                    time.Time.Humanize()).ConfigureAwait(false);
            }
            catch
            {
                await ReplyErrorLocalizedAsync("mute_error").ConfigureAwait(false);
            }
        }

        [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild),
         UserPerm(GuildPermission.ManageRoles), Priority(1)]
        public async Task ChatMute(StoopidTime time, IGuildUser user, [Remainder] string reason = "")
        {
            if (time.Time < TimeSpan.FromMinutes(1) || time.Time > TimeSpan.FromDays(49))
                return;
            try
            {
                if (!await VerifyMutePermissions((IGuildUser) ctx.User, user))
                    return;

                await Service.TimedMute(user, ctx.User, time.Time, MuteType.Chat, reason).ConfigureAwait(false);
                await ReplyConfirmLocalizedAsync("user_chat_mute_time", Format.Bold(user.ToString()),
                    time.Time.Humanize()).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Warning(ex.ToString());
                await ReplyErrorLocalizedAsync("mute_error").ConfigureAwait(false);
            }
        }

        [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild),
         UserPerm(GuildPermission.MuteMembers), Priority(1)]
        public async Task VoiceMute(IGuildUser user, [Remainder] string reason = "")
        {
            try
            {
                if (!await VerifyMutePermissions((IGuildUser) ctx.User, user))
                    return;

                await Service.MuteUser(user, ctx.User, MuteType.Voice, reason).ConfigureAwait(false);
                await ReplyConfirmLocalizedAsync("user_voice_mute", Format.Bold(user.ToString()))
                    .ConfigureAwait(false);
            }
            catch
            {
                await ReplyErrorLocalizedAsync("mute_error").ConfigureAwait(false);
            }
        }

        [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild),
         UserPerm(GuildPermission.MuteMembers)]
        public async Task VoiceUnmute(IGuildUser user, [Remainder] string reason = "")
        {
            try
            {
                await Service.UnmuteUser(user.GuildId, user.Id, ctx.User, MuteType.Voice, reason)
                    .ConfigureAwait(false);
                await ReplyConfirmLocalizedAsync("user_voice_unmute", Format.Bold(user.ToString()))
                    .ConfigureAwait(false);
            }
            catch
            {
                await ReplyErrorLocalizedAsync("mute_error").ConfigureAwait(false);
            }
        }
    }
}