using System.Net;
using Discord;
using Discord.Commands;
using Discord.Net;
using Discord.WebSocket;
using Fergun.Interactive;
using Fergun.Interactive.Pagination;
using Mewdeko._Extensions;
using Mewdeko.Common;
using Mewdeko.Common.Attributes;
using Mewdeko.Database.Models;
using Mewdeko.Modules.Administration.Services;
using Serilog;
using SixLabors.ImageSharp.PixelFormats;
using Color = SixLabors.ImageSharp.Color;

namespace Mewdeko.Modules.Administration;

public partial class Administration
{
    public class RoleCommands : MewdekoSubmodule<RoleCommandsService>
    {
        public enum Exclude
        {
            Excl
        }

        private readonly IServiceProvider _services;
        private readonly InteractiveService _interactivity;

        public RoleCommands(IServiceProvider services, InteractiveService intserv)
        {
            _services = services;
            _interactivity = intserv;
        }

        public async Task InternalReactionRoles(bool exclusive, ulong? messageId, params string[] input)
        {
            var target = messageId is { } msgId
                ? await ctx.Channel.GetMessageAsync(msgId).ConfigureAwait(false)
                : (await ctx.Channel.GetMessagesAsync(2).FlattenAsync().ConfigureAwait(false))
                .Skip(1)
                .FirstOrDefault();


            if (input.Length % 2 != 0)
                return;

            var grp = 0;
            var results = input
                .GroupBy(_ => grp++ / 2)
                .Select(async x =>
                {
                    var inputRoleStr = x.First();
                    var roleReader = new RoleTypeReader<SocketRole>();
                    var roleResult = await roleReader.ReadAsync(ctx, inputRoleStr, _services);
                    if (!roleResult.IsSuccess)
                    {
                        Log.Warning("Role {0} not found.", inputRoleStr);
                        return null;
                    }

                    var role = (IRole) roleResult.BestMatch;
                    if (role.Position > ((IGuildUser) ctx.User).GetRoles().Select(r => r.Position).Max()
                        && ctx.User.Id != ctx.Guild.OwnerId)
                        return null;
                    var emote = x.Last().ToIEmote();
                    return new {role, emote};
                })
                .Where(x => x != null);

            var all = await Task.WhenAll(results);

            if (!all.Any())
                return;

            foreach (var x in all)
            {
                try
                {
                    if (target != null)
                        await target.AddReactionAsync(x.emote, new RequestOptions
                        {
                            RetryMode = RetryMode.Retry502 | RetryMode.RetryRatelimit
                        }).ConfigureAwait(false);
                }
                catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.BadRequest)
                {
                    await ReplyErrorLocalizedAsync("reaction_cant_access", Format.Code(x.emote.ToString()));
                    return;
                }

                await Task.Delay(500).ConfigureAwait(false);
            }

            if (target != null && Service.Add(ctx.Guild.Id, new ReactionRoleMessage
                {
                    Exclusive = exclusive,
                    MessageId = target.Id,
                    ChannelId = target.Channel.Id,
                    ReactionRoles = all.Select(x => new ReactionRole
                    {
                        EmoteName = x.emote.ToString(),
                        RoleId = x.role.Id
                    }).ToList()
                }))
                await ctx.OkAsync();
            else
                await ReplyErrorLocalizedAsync("reaction_roles_full").ConfigureAwait(false);
        }

        [MewdekoCommand, Aliases, RequireContext(ContextType.Guild), BotPerm(GuildPermission.ManageRoles), Priority(0)]
        public Task ReactionRoles(ulong messageId, params string[] input) => InternalReactionRoles(false, messageId, input);

        [MewdekoCommand, Aliases, RequireContext(ContextType.Guild), UserPerm(GuildPermission.ManageRoles),
         BotPerm(GuildPermission.ManageRoles), Priority(1)]
        public Task ReactionRoles(ulong messageId, Exclude _, params string[] input) => InternalReactionRoles(true, messageId, input);

        [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild),
         UserPerm(GuildPermission.ManageRoles), BotPerm(GuildPermission.ManageRoles), Priority(0)]
        public Task ReactionRoles(params string[] input) => InternalReactionRoles(false, null, input);

        [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild),
         UserPerm(GuildPermission.ManageRoles), BotPerm(GuildPermission.ManageRoles), Priority(1)]
        public Task ReactionRoles(Exclude _, params string[] input) => InternalReactionRoles(true, null, input);

        [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild),
         UserPerm(GuildPermission.ManageRoles)]
        public async Task ReactionRolesList()
        {
            var embed = new EmbedBuilder()
                .WithOkColor();
            if (!Service.Get(ctx.Guild.Id, out var rrs) ||
                !rrs.Any())
            {
                embed.WithDescription(GetText("no_reaction_roles"));
            }
            else
            {
                var paginator = new LazyPaginatorBuilder()
                    .AddUser(ctx.User)
                    .WithPageFactory(PageFactory)
                    .WithFooter(PaginatorFooter.PageNumber | PaginatorFooter.Users)
                    .WithMaxPageIndex(rrs.Count - 1)
                    .WithDefaultEmotes()
                    .Build();

                await _interactivity.SendPaginatorAsync(paginator, Context.Channel, TimeSpan.FromMinutes(60));

                async Task<PageBuilder> PageFactory(int page)
                {
                    var rr = rrs.Skip(page).FirstOrDefault();
                    var g = ctx.Guild;
                    var ch = await g.GetTextChannelAsync(rr.ChannelId);
                    IUserMessage msg = null;
                    if (ch is not null)
                        msg = (await ch.GetMessageAsync(rr.MessageId)) as IUserMessage;
                    var eb = new PageBuilder().WithOkColor();
                    return
                        eb.AddField("ID", rr.Index + 1).AddField($"Roles ({rr.ReactionRoles.Count})",
                                string.Join(",",
                                    rr.ReactionRoles.Select(x => $"{x.EmoteName} {g.GetRole(x.RoleId).Mention}")))
                            .AddField("Users can select more than one role", !rr.Exclusive)
                            .AddField("Was Deleted?", msg == null ? "Yes" : "No")
                            .AddField("Message Link",
                                msg == null ? "None, Message was Deleted." : $"[Link]({msg.GetJumpUrl()})");
                }
            }
        }

        [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild),
         UserPerm(GuildPermission.ManageRoles)]
        public async Task ReactionRolesRemove(int index)
        {
            if (index < 1 ||
                !Service.Get(ctx.Guild.Id, out var rrs) ||
                !rrs.Any() || rrs.Count < index)
                return;
            index--;
            Service.Remove(ctx.Guild.Id, index);
            await ReplyConfirmLocalizedAsync("reaction_role_removed", index + 1).ConfigureAwait(false);
        }

        [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild),
         UserPerm(GuildPermission.ManageRoles), BotPerm(GuildPermission.ManageRoles)]
        public async Task SetRole(IRole roleToAdd, [Remainder] IGuildUser targetUser)
        {
            var runnerUser = (IGuildUser) ctx.User;
            var runnerMaxRolePosition = runnerUser.GetRoles().Max(x => x.Position);
            if (ctx.User.Id != ctx.Guild.OwnerId && runnerMaxRolePosition <= roleToAdd.Position)
                return;
            try
            {
                await targetUser.AddRoleAsync(roleToAdd).ConfigureAwait(false);

                await ReplyConfirmLocalizedAsync("setrole", Format.Bold(roleToAdd.Name),
                        Format.Bold(targetUser.ToString()))
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error in setrole command");
                await ReplyErrorLocalizedAsync("setrole_err").ConfigureAwait(false);
            }
        }

        [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild),
         UserPerm(GuildPermission.ManageRoles), BotPerm(GuildPermission.ManageRoles)]
        public async Task RemoveRole(IRole roleToRemove, [Remainder] IGuildUser targetUser)
        {
            var runnerUser = (IGuildUser) ctx.User;
            if (ctx.User.Id != runnerUser.Guild.OwnerId &&
                runnerUser.GetRoles().Max(x => x.Position) <= roleToRemove.Position)
                return;
            try
            {
                await targetUser.RemoveRoleAsync(roleToRemove).ConfigureAwait(false);
                await ReplyConfirmLocalizedAsync("remrole", Format.Bold(roleToRemove.Name),
                    Format.Bold(targetUser.ToString())).ConfigureAwait(false);
            }
            catch
            {
                await ReplyErrorLocalizedAsync("remrole_err").ConfigureAwait(false);
            }
        }

        [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild),
         UserPerm(GuildPermission.ManageRoles), BotPerm(GuildPermission.ManageRoles)]
        public async Task RenameRole(IRole roleToEdit, [Remainder] string newname)
        {
            var guser = (IGuildUser) ctx.User;
            if (ctx.User.Id != guser.Guild.OwnerId && guser.GetRoles().Max(x => x.Position) <= roleToEdit.Position)
                return;
            try
            {
                if (roleToEdit.Position > (await ctx.Guild.GetCurrentUserAsync().ConfigureAwait(false)).GetRoles()
                    .Max(r => r.Position))
                {
                    await ReplyErrorLocalizedAsync("renrole_perms").ConfigureAwait(false);
                    return;
                }

                await roleToEdit.ModifyAsync(g => g.Name = newname).ConfigureAwait(false);
                await ReplyConfirmLocalizedAsync("renrole").ConfigureAwait(false);
            }
            catch (Exception)
            {
                await ReplyErrorLocalizedAsync("renrole_err").ConfigureAwait(false);
            }
        }

        [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild),
         UserPerm(GuildPermission.ManageRoles), BotPerm(GuildPermission.ManageRoles)]
        public async Task RemoveAllRoles([Remainder] IGuildUser user)
        {
            var guser = (IGuildUser) ctx.User;

            var userRoles = user.GetRoles()
                .Where(x => !x.IsManaged && x != x.Guild.EveryoneRole)
                .ToList();

            if (user.Id == ctx.Guild.OwnerId || (ctx.User.Id != ctx.Guild.OwnerId &&
                                                 guser.GetRoles().Max(x => x.Position) <= userRoles.Max(x => x.Position)))
                return;
            try
            {
                await user.RemoveRolesAsync(userRoles).ConfigureAwait(false);
                await ReplyConfirmLocalizedAsync("rar", Format.Bold(user.ToString())).ConfigureAwait(false);
            }
            catch (Exception)
            {
                await ReplyErrorLocalizedAsync("rar_err").ConfigureAwait(false);
            }
        }

        [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild),
         UserPerm(GuildPermission.ManageRoles), BotPerm(GuildPermission.ManageRoles)]
        public async Task CreateRole([Remainder] string? roleName = null)
        {
            if (string.IsNullOrWhiteSpace(roleName))
                return;

            var r = await ctx.Guild.CreateRoleAsync(roleName, isMentionable: false).ConfigureAwait(false);
            await ReplyConfirmLocalizedAsync("cr", Format.Bold(r.Name)).ConfigureAwait(false);
        }

        [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild),
         UserPerm(GuildPermission.ManageRoles), BotPerm(GuildPermission.ManageRoles)]
        public async Task DeleteRole([Remainder] IRole role)
        {
            var guser = (IGuildUser) ctx.User;
            if (ctx.User.Id != guser.Guild.OwnerId
                && guser.GetRoles().Max(x => x.Position) <= role.Position)
                return;

            await role.DeleteAsync().ConfigureAwait(false);
            await ReplyConfirmLocalizedAsync("dr", Format.Bold(role.Name)).ConfigureAwait(false);
        }

        [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild),
         UserPerm(GuildPermission.ManageRoles), BotPerm(GuildPermission.ManageRoles)]
        public async Task RoleHoist(IRole role)
        {
            var newHoisted = !role.IsHoisted;
            await role.ModifyAsync(r => r.Hoist = newHoisted).ConfigureAwait(false);
            if (newHoisted)
                await ReplyConfirmLocalizedAsync("rolehoist_enabled", Format.Bold(role.Name)).ConfigureAwait(false);
            else
                await ReplyConfirmLocalizedAsync("rolehoist_disabled", Format.Bold(role.Name))
                    .ConfigureAwait(false);
        }

        [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild), Priority(1)]
        public async Task RoleColor([Remainder] IRole role) =>
            await ctx.Channel.SendConfirmAsync("Role Color", role.Color.RawValue.ToString("x6"))
                     .ConfigureAwait(false);

        [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild),
         UserPerm(GuildPermission.ManageRoles), BotPerm(GuildPermission.ManageRoles), Priority(0)]
        public async Task RoleColor(IRole role, Color color)
        {
            try
            {
                var rgba32 = color.ToPixel<Rgba32>();
                await role.ModifyAsync(r => r.Color = new Discord.Color(rgba32.R, rgba32.G, rgba32.B))
                    .ConfigureAwait(false);
                await ReplyConfirmLocalizedAsync("rc", Format.Bold(role.Name)).ConfigureAwait(false);
            }
            catch (Exception)
            {
                await ReplyErrorLocalizedAsync("rc_perms").ConfigureAwait(false);
            }
        }
    }
}