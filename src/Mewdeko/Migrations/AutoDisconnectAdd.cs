﻿using Microsoft.EntityFrameworkCore.Migrations;

namespace Mewdeko.Migrations
{
    public partial class AutoDisconnectAdd : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable("MusicPlayerSettings");
            migrationBuilder.CreateTable(
                name: "MusicPlayerSettings",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GuildId = table.Column<ulong>(nullable: false),
                    PlayerRepeat = table.Column<int>(nullable: false),
                    MusicChannelId = table.Column<ulong>(nullable: true),
                    Volume = table.Column<int>(nullable: false, defaultValue: 100)
                        .Annotation("Sqlite:Autoincrement", true),
                    AutoDisconnect = table.Column<int>( defaultValue: 0, nullable: false),
                    AutoPlay = table.Column<bool>(defaultValue: 0, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MusicPlayerSettings", x => x.Id);
                });
    
            migrationBuilder.CreateIndex(
                name: "IX_MusicPlayerSettings_GuildId",
                table: "MusicPlayerSettings",
                column: "GuildId",
                unique: true);
        }

        
    }
}