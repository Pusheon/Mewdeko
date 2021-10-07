﻿using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Mewdeko._Extensions;
using Mewdeko.Common;
using Mewdeko.Common.Attributes;
using Mewdeko.Modules.Administration.Services;

namespace Mewdeko.Modules.Administration
{
    public partial class Administration
    {
        [Group]
        public class AutoAssignRoleCommands : MewdekoSubmodule<AutoAssignRoleService>
        {
            [MewdekoCommand]
            [Usage]
            [Description]
            [Aliases]
            [RequireContext(ContextType.Guild)]
            [UserPerm(GuildPerm.ManageRoles)]
            public async Task AutoAssignRole([Remainder] IRole role)
            {
                var guser = (IGuildUser)ctx.User;
                if (role.Id == ctx.Guild.EveryoneRole.Id)
                    return;

                // the user can't aar the role which is higher or equal to his highest role
                if (ctx.User.Id != guser.Guild.OwnerId && guser.GetRoles().Max(x => x.Position) <= role.Position)
                {
                    await ReplyErrorLocalizedAsync("hierarchy");
                    return;
                }

                var roles = await _service.ToggleAarAsync(ctx.Guild.Id, role.Id);
                if (roles.Count == 0)
                    await ReplyConfirmLocalizedAsync("aar_disabled");
                else if (roles.Contains(role.Id))
                    await AutoAssignRole();
                else
                    await ReplyConfirmLocalizedAsync("aar_role_removed", Format.Bold(role.Mention));
            }

            [MewdekoCommand]
            [Usage]
            [Description]
            [Aliases]
            [RequireContext(ContextType.Guild)]
            [UserPerm(GuildPerm.ManageRoles)]
            public async Task AutoAssignRole()
            {
                if (!_service.TryGetNormalRoles(ctx.Guild.Id, out var roles))
                {
                    await ReplyConfirmLocalizedAsync("aar_none");
                    return;
                }

                var existing = roles.Select(rid => ctx.Guild.GetRole(rid)).Where(r => !(r is null))
                    .ToList();

                if (existing.Count != roles.Count)
                    await _service.SetAarRolesAsync(ctx.Guild.Id, existing.Select(x => x.Id));

                await ReplyConfirmLocalizedAsync("aar_roles",
                    '\n' + existing.Select(x => Format.Bold(x.Mention))
                        .JoinWith("\n"));
            }

            [MewdekoCommand]
            [Usage]
            [Description]
            [Aliases]
            [RequireContext(ContextType.Guild)]
            [UserPerm(GuildPerm.ManageRoles)]
            public async Task AutoAssignBotRole([Remainder] IRole role)
            {
                var guser = (IGuildUser)ctx.User;
                if (role.Id == ctx.Guild.EveryoneRole.Id)
                    return;

                // the user can't aar the role which is higher or equal to his highest role
                if (ctx.User.Id != guser.Guild.OwnerId && guser.GetRoles().Max(x => x.Position) <= role.Position)
                {
                    await ReplyErrorLocalizedAsync("hierarchy");
                    return;
                }

                var roles = await _service.ToggleAabrAsync(ctx.Guild.Id, role.Id);
                if (roles.Count == 0)
                    await ReplyConfirmLocalizedAsync("aabr_disabled");
                else if (roles.Contains(role.Id))
                    await AutoAssignBotRole();
                else
                    await ReplyConfirmLocalizedAsync("aabr_role_removed", Format.Bold(role.Mention));
            }

            [MewdekoCommand]
            [Usage]
            [Description]
            [Aliases]
            [RequireContext(ContextType.Guild)]
            [UserPerm(GuildPerm.ManageRoles)]
            public async Task AutoAssignBotRole()
            {
                if (!_service.TryGetBotRoles(ctx.Guild.Id, out var roles))
                {
                    await ReplyConfirmLocalizedAsync("aabr_none");
                    return;
                }

                var existing = roles.Select(rid => ctx.Guild.GetRole(rid)).Where(r => !(r is null))
                    .ToList();

                if (existing.Count != roles.Count)
                    await _service.SetAabrRolesAsync(ctx.Guild.Id, existing.Select(x => x.Id));

                await ReplyConfirmLocalizedAsync("aabr_roles",
                    '\n' + existing.Select(x => Format.Bold(x.Mention))
                        .JoinWith("\n"));
            }
        }
    }
}