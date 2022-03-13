using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Humanizer;
using Mewdeko._Extensions;
using Mewdeko.Common;
using Mewdeko.Common.Attributes;
using Mewdeko.Database.Extensions;
using Mewdeko.Modules.Utility.Services;

namespace Mewdeko.Modules.Utility;

public partial class Utility
{
    [Group]
    public class InfoCommands : MewdekoSubmodule<UtilityService>
    {
        private readonly DiscordSocketClient _client;
        private readonly IStatsService _stats;

        public InfoCommands(DiscordSocketClient client, IStatsService stats)
        {
            _client = client;
            _stats = stats;
        }

        [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild)]
        public async Task RInfo(IRole role)
        {
            var eb = new EmbedBuilder().WithOkColor().WithTitle(role.Name)
                                       .AddField("Users in role",
                                           (await ctx.Guild.GetUsersAsync()).Count(x => x.RoleIds.Contains(role.Id)))
                                       .AddField("Is Mentionable", role.IsMentionable)
                                       .AddField("Is Hoisted", role.IsHoisted).AddField("Color", role.Color.RawValue)
                                       .AddField("Is Managed", role.IsManaged)
                                       .AddField("Permissions", string.Join(",", role.Permissions))
                                       .WithThumbnailUrl(role.GetIconUrl());
            await ctx.Channel.SendMessageAsync(embed: eb.Build());
        }

        [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild)]
        public async Task VInfo([Remainder] IVoiceChannel? channel = null)
        {
            var voiceChannel = ((IGuildUser) ctx.User).VoiceChannel;
            var eb = new EmbedBuilder();
            switch (voiceChannel)
            {
                case null when channel == null:
                    await ctx.Channel.SendErrorAsync(
                        "You arent in a voice channel, and you haven't mentioned either to use this command!");
                    return;
                // ReSharper disable once ConditionIsAlwaysTrueOrFalse
                case null when channel is not null:
                    eb.WithTitle(channel.Name);
                    eb.AddField("Users", (await channel.GetUsersAsync().FlattenAsync()).Count());
                    eb.AddField("Created On", channel.CreatedAt);
                    eb.AddField("Bitrate", channel.Bitrate);
                    eb.AddField("User Limit", channel.UserLimit == null ? "Infinite" : channel.UserLimit);
                    eb.AddField("Channel ID", channel.Id);
                    eb.WithOkColor();
                    await ctx.Channel.SendMessageAsync(embed: eb.Build());
                    break;
            }

            if (voiceChannel is not null && channel is not null)
            {
                eb.WithTitle(channel.Name);
                eb.AddField("Users", (await channel.GetUsersAsync().FlattenAsync()).Count());
                eb.AddField("Created On", channel.CreatedAt);
                eb.AddField("Bitrate", channel.Bitrate);
                eb.AddField("User Limit", channel.UserLimit == null ? "Infinite" : channel.UserLimit);
                eb.AddField("Channel ID", channel.Id);
                eb.WithOkColor();
                await ctx.Channel.SendMessageAsync(embed: eb.Build());
            }

            if (voiceChannel is not null && channel is null)
            {
                eb.WithTitle(voiceChannel.Name);
                eb.AddField("Users", (await voiceChannel.GetUsersAsync().FlattenAsync()).Count());
                eb.AddField("Created On", voiceChannel.CreatedAt);
                eb.AddField("Bitrate", voiceChannel.Bitrate);
                eb.AddField("User Limit", voiceChannel.UserLimit == null ? "Infinite" : voiceChannel.UserLimit);
                eb.AddField("Channel ID", voiceChannel.Id);
                eb.WithOkColor();
                await ctx.Channel.SendMessageAsync(embed: eb.Build());
            }
        }

        [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild)]
        public async Task Fetch(ulong id)
        {
            var usr = await _client.Rest.GetUserAsync(id);
            if (usr is null)
            {
                var chans = await ctx.Guild.GetTextChannelsAsync();
                IUserMessage message = null;
                foreach (var i in chans)
                {
                    var e = await i.GetMessageAsync(id);
                    if (e is not null)
                        message = e as IUserMessage;
                }

                var eb = new EmbedBuilder()
                    .WithTitle("Message Info");
                if (message.Embeds.Any()) eb.AddField("Embeds", message.Embeds.Count);

                if (!string.IsNullOrEmpty(message.Content))
                    eb.AddField("Message Content (Limited to 60 characters)", message.Content.Truncate(60));

                eb.WithAuthor(message.Author);
                eb.AddField("Time Sent", message.Timestamp);
                await ctx.Channel.SendMessageAsync(embed: eb.Build());
            }
            else
            {
                var embed = new EmbedBuilder()
                    .WithTitle("info for fetched user")
                    .WithDescription($"User: {usr.Username}#{usr.Discriminator}\nUser Created At: {usr.CreatedAt}")
                    .WithImageUrl(usr.RealAvatarUrl().ToString())
                    .WithOkColor();
                await ctx.Channel.EmbedAsync(embed).ConfigureAwait(false);
            }
        }

        [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild)]
        public async Task ServerInfo(string? guildName = null)
        {
            var channel = (ITextChannel) ctx.Channel;
            guildName = guildName?.ToUpperInvariant();
            SocketGuild guild;
            if (string.IsNullOrWhiteSpace(guildName))
                guild = (SocketGuild) channel.Guild;
            else
                guild = _client.Guilds.FirstOrDefault(
                    g => g.Name.ToUpperInvariant() == guildName.ToUpperInvariant());
            if (guild == null)
                return;
            var ownername = guild.GetUser(guild.OwnerId);
            var textchn = guild.TextChannels.Count;
            var voicechn = guild.VoiceChannels.Count;

            var createdAt = new DateTime(2015, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(guild.Id >> 22);
            var list = new List<string>();

            var component = new ComponentBuilder().WithButton("More Info", "moreinfo");
            var embed = new EmbedBuilder()
                .WithAuthor(eab => eab.WithName(GetText("server_info")))
                .WithTitle(guild.Name)
                .AddField("Id", guild.Id.ToString())
                .AddField("Owner", ownername.Mention)
                .AddField("Total Users", guild.Users.Count.ToString())
                .WithColor(Mewdeko.OkColor);
            if (guild.SplashUrl != null)
                embed.WithImageUrl($"{guild.SplashUrl}?size=2048");
            if (Uri.IsWellFormedUriString(guild.IconUrl, UriKind.Absolute))
                embed.WithThumbnailUrl(guild.IconUrl);
            if (guild.Emotes.Any())
                embed.AddField(fb =>
                    fb.WithName($"{GetText("custom_emojis")}({guild.Emotes.Count})")
                        .WithValue(string.Join(" ", guild.Emotes
                                .Shuffle()
                                .Take(30)
                                .Select(e => $"{e}"))
                            .TrimTo(1024)));
            var msg = await ctx.Channel.SendMessageAsync(embed: embed.Build(), components: component.Build());
            var input = await GetButtonInputAsync(ctx.Channel.Id, msg.Id, ctx.User.Id);
            if (input == "moreinfo")
            {
                var vals = Enum.GetValues(typeof(GuildFeature)).Cast<GuildFeature>();
                var setFeatures = vals.Where(x => guild.Features.Value.HasFlag(x));
                embed
                    .AddField("Bots", (await ctx.Guild.GetUsersAsync()).Count(x => x.IsBot))
                    .AddField("Users", (await ctx.Guild.GetUsersAsync()).Count(x => !x.IsBot))
                    .AddField("Text Channels", textchn.ToString())
                    .AddField("Voice Channels", voicechn.ToString())
                    .AddField("Created On", $"{createdAt:MM/dd/yyyy HH:mm}")
                    .AddField("Roles", (guild.Roles.Count - 1).ToString())
                    .AddField("Server Features", Format.Code(string.Join("\n", setFeatures)));
                await msg.ModifyAsync(x =>
                {
                    x.Embed = embed.Build();
                    x.Components = null;
                });
            }
        }

        [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild)]
        public async Task ChannelInfo(ITextChannel? channel = null)
        {
            var ch = channel ?? (ITextChannel) ctx.Channel;
            var createdAt = new DateTime(2015, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(ch.Id >> 22);
            var embed = new EmbedBuilder()
                .WithTitle(ch.Name)
                .AddField(GetText("id"), ch.Id.ToString())
                .AddField(GetText("created_at"), $"{createdAt:dd.MM.yyyy HH:mm}")
                .AddField(GetText("users"), (await ch.GetUsersAsync().FlattenAsync()).Count())
                .AddField("Topic", ch.Topic ?? "None")
                .WithColor(Mewdeko.OkColor);
            await ctx.Channel.EmbedAsync(embed).ConfigureAwait(false);
        }

        [MewdekoCommand, Usage, Description, Aliases, RequireContext(ContextType.Guild)]
        public async Task UserInfo(IGuildUser? usr = null)
        {
            var component = new ComponentBuilder().WithButton("More Info", "moreinfo");
            var user = usr ?? ctx.User as IGuildUser;
            var userbanner = (await _client.Rest.GetUserAsync(user.Id)).GetBannerUrl(size: 2048);
            string serverUserType;
            if (user.GuildPermissions.ManageMessages)
                serverUserType = "Helper";
            if (user.GuildPermissions.BanMembers)
                serverUserType = "Moderator";
            if (user.GuildPermissions.Administrator)
                serverUserType = "Administrator";
            else serverUserType = "Regular User";

            var embed = new EmbedBuilder()
                .AddField("Username", user.ToString())
                .WithOkColor();

            if (!string.IsNullOrWhiteSpace(user.Nickname))
                embed.AddField("Nickname", user.Nickname);

            embed.AddField("User Id", user.Id)
                .AddField("User Type", serverUserType)
                .AddField("Joined Server", user.JoinedAt?.ToString("MM/dd/yyyy HH:mm"))
                .AddField("Joined Discord", $"{user.CreatedAt:MM/dd/yyyy HH:mm}")
                .AddField("Role Count", user.GetRoles().Count(r => r.Id != r.Guild.EveryoneRole.Id));

            if (user.Activities.Any())
                embed.AddField("Activities",
                    string.Join("\n", user.Activities.Select(x => string.Format($"{x.Name}: {x.Details ?? ""}"))));
            var av = user.RealAvatarUrl();
            if (av != null && av.IsAbsoluteUri)
                if (userbanner is not null)
                {
                    embed.WithThumbnailUrl(av.ToString());
                    embed.WithImageUrl(userbanner);
                }
                else
                {
                    embed.WithImageUrl(av.ToString());
                }

            var msg = await ctx.Channel.SendMessageAsync(embed: embed.Build(), components: component.Build());
            var input = await GetButtonInputAsync(ctx.Channel.Id, msg.Id, ctx.User.Id);
            if (input == "moreinfo")
            {
                if (user.GetRoles().Any())
                    embed.AddField("Roles",
                        string.Join("", user.GetRoles().OrderBy(x => x.Position).Select(x => x.Mention)));
                embed.AddField("Deafened", user.IsDeafened);
                embed.AddField("Is VC Muted", user.IsMuted);
                embed.AddField("Is Server Muted", user.GetRoles().Contains(MuteRole));
                await msg.ModifyAsync(x =>
                {
                    x.Embed = embed.Build();
                    x.Components = null;
                });
            }
        }
    }
}