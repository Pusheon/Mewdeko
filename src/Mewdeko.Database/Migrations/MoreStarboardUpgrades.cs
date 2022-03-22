using Microsoft.EntityFrameworkCore.Migrations;

namespace Mewdeko.Database.Migrations;

public partial class MoreStarboardUpgrades : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>("StarboardRemoveOnDelete", "GuildConfigs", defaultValue: false);
        migrationBuilder.AddColumn<bool>("StarboardRemoveOnReactionsClear", "GuildConfigs", defaultValue: false);
        migrationBuilder.AddColumn<bool>("StarboardRemoveOnBelowThreshold", "GuildConfigs", defaultValue: true);
    }
}