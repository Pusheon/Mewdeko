using Microsoft.EntityFrameworkCore.Migrations;

namespace Mewdeko.Database.Migrations;

public partial class AnotherStarboardUpgrade : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) 
        => migrationBuilder.AddColumn<bool>("StarboardAllowBots", "GuildConfigs", defaultValue: true);
}