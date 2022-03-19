using Mewdeko.Database.Common;

// ReSharper disable InconsistentNaming

namespace Mewdeko.Database.Models;

public class GuildConfig : DbEntity
{
    public ulong GuildId { get; set; }

    public string Prefix { get; set; } = null;
    public ulong StaffRole { get; set; }
    public ulong GameMasterRole { get; set; }
    public bool DeleteMessageOnCommand { get; set; }
    public string WarnMessage { get; set; } = "-";
    public HashSet<DelMsgOnCmdChannel> DelMsgOnCmdChannels { get; set; } = new();
    public string AutoAssignRoleId { get; set; } = "0";
    public bool StarboardAllowBots = true;
    public bool StarboardRemoveOnDelete = false;
    public bool StarboardRemoveOnReactionsClear = false;
    public bool StarboardRemoveOnBelowThreshold = true;
    
    public string AutoBotRoleIds { get; set; }
    public int GBEnabled { get; set; } = 1;
    public int GBAction { get; set; } = 1;
    public ulong ConfessionLogChannel { get; set; } = 0;
    public ulong ConfessionChannel { get; set; } = 0;
    public string ConfessionBlacklist { get; set; } = "0";
    public int MultiGreetType { get; set; } = 0;
    public int MinSuggestLength { get; set; } = 0;
    public int MaxSuggestLength { get; set; } = 4098;
    public string StarboardCheckChannels { get; set; } = "0";
    public ulong MemberRole { get; set; } = 0;
    public bool UseStarboardBlacklist { get; set; } = true;
    public bool StarboardAllowBots { get; set; } = true;
    public string TOpenMessage { get; set; } = "none";
    public string GStartMessage { get; set; } = "none";
    public string GEndMessage { get; set; } = "none";
    public string GWinMessage { get; set; } = "none";
    public ulong WarnlogChannelId { get; set; } = 0;
    public ulong MiniWarnlogChannelId { get; set; } = 0;
    public bool SendBoostMessage { get; set; }
    public string GRolesBlacklist { get; set; } = "-";
    public string GUsersBlacklist { get; set; } = "-";
    public string BoostMessage { get; set; } = "%user% just boosted this server!";
    public ulong BoostMessageChannelId { get; set; }
    public int BoostMessageDeleteAfter { get; set; }
    public string GiveawayEmote { get; set; }
    public ulong TicketChannel { get; set; } = 0;
    public ulong TicketCategory { get; set; } = 0;
    public bool snipeset { get; set; }
    public string SuggestEmotes { get; set; }
    public int AfkLength { get; set; } = 250;
    public string SuggestMessage { get; set; } = "";
    public string DenyMessage { get; set; } = "";
    public string AcceptMessage { get; set; } = "";
    public string ImplementMessage { get; set; } = "";
    public string ConsiderMessage { get; set; } = "";
    public string AfkMessage { get; set; } = "-";
    public int XpTxtTimeout { get; set; }
    public int XpTxtRate { get; set; }
    public int XpVoiceRate { get; set; }
    public int XpVoiceTimeout { get; set; }
    public List<WarningPunishment2> WarnPunishments2 { get; set; } = new();
    public int Stars { get; set; } = 3;
    public int AfkType { get; set; } = 2;
    public AntiAltSetting AntiAltSetting { get; set; }
    public string AfkDisabledChannels { get; set; }
    public int AfkDel { get; set; }
    public int AfkTimeout { get; set; } = 20;
    public ulong Joins { get; set; }
    public ulong Leaves { get; set; }
    public string Star2 { get; set; } = "⭐";
    public ulong StarboardChannel { get; set; }
    public int RepostThreshold { get; set; }
    public int PreviewLinks { get; set; }
    public ulong ReactChannel { get; set; }
    public ulong sugnum { get; set; }
    public ulong sugchan { get; set; }
    public int fwarn { get; set; }
    public int invwarn { get; set; }
    public int removeroles { get; set; }
    public bool AutoDeleteGreetMessages { get; set; }
    public bool AutoDeleteByeMessages { get; set; }
    public int AutoDeleteGreetMessagesTimer { get; set; } = 30;
    public int AutoDeleteByeMessagesTimer { get; set; } = 30;

    public ulong GreetMessageChannelId { get; set; }
    public ulong ByeMessageChannelId { get; set; }
    public string GreetHook { get; set; } = "";
    public string LeaveHook { get; set; } = "";

    public bool SendDmGreetMessage { get; set; }
    public string DmGreetMessageText { get; set; } = "Welcome to the %server% server, %user%!";

    public bool SendChannelGreetMessage { get; set; }
    public string ChannelGreetMessageText { get; set; } = "Welcome to the %server% server, %user%!";

    public bool SendChannelByeMessage { get; set; }
    public string ChannelByeMessageText { get; set; } = "%user% has left!";

    public LogSetting LogSetting { get; set; } = new();
    public bool ExclusiveSelfAssignedRoles { get; set; }
    public bool AutoDeleteSelfAssignedRoleMessages { get; set; }

    //stream notifications
    public HashSet<FollowedStream> FollowedStreams { get; set; } = new();

    //currencyGeneration
    public HashSet<GcChannelId> GenerateCurrencyChannelIds { get; set; } = new();

    //permissions
    public List<Permissionv2> Permissions { get; set; }
    public bool VerbosePermissions { get; set; } = true;
    public string PermissionRole { get; set; } = null;

    public HashSet<CommandCooldown> CommandCooldowns { get; set; } = new();

    //filtering
    public bool FilterInvites { get; set; }
    public bool FilterLinks { get; set; }
    public HashSet<FilterChannelId> FilterInvitesChannelIds { get; set; } = new();
    public HashSet<FilterLinksChannelId> FilterLinksChannelIds { get; set; } = new();


    public bool FilterWords { get; set; }
    public HashSet<FilteredWord> FilteredWords { get; set; } = new();
    public HashSet<FilterChannelId> FilterWordsChannelIds { get; set; } = new();

    public HashSet<MutedUserId> MutedUsers { get; set; } = new();

    public string MuteRoleName { get; set; }
    public ulong CleverbotChannel { get; set; }
    public List<Repeater> GuildRepeaters { get; set; } = new();

    public AntiRaidSetting AntiRaidSetting { get; set; }
    public AntiSpamSetting AntiSpamSetting { get; set; }

    public string Locale { get; set; } = null;
    public string TimeZoneId { get; set; } = null;

    public HashSet<UnmuteTimer> UnmuteTimers { get; set; } = new();
    public HashSet<UnbanTimer> UnbanTimer { get; set; } = new();
    public HashSet<UnroleTimer> UnroleTimer { get; set; } = new();
    public HashSet<VcRoleInfo> VcRoleInfos { get; set; }
    public HashSet<CommandAlias> CommandAliases { get; set; } = new();
    public List<WarningPunishment> WarnPunishments { get; set; } = new();
    public bool WarningsInitialized { get; set; }
    public HashSet<NsfwBlacklitedTag> NsfwBlacklistedTags { get; set; } = new();

    public List<ShopEntry> ShopEntries { get; set; }
    public ulong? GameVoiceChannel { get; set; } = null;
    public bool VerboseErrors { get; set; } = true;

    public StreamRoleSettings StreamRole { get; set; }

    public XpSettings XpSettings { get; set; }
    public List<FeedSub> FeedSubs { get; set; } = new();
    public IndexedCollection<ReactionRoleMessage> ReactionRoleMessages { get; set; } = new();
    public bool NotifyStreamOffline { get; set; }
    public List<GroupName> SelfAssignableRoleGroupNames { get; set; }
    public int WarnExpireHours { get; set; } = 0;
    public WarnExpireAction WarnExpireAction { get; set; } = WarnExpireAction.Clear;
}