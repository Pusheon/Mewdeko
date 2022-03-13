using Microsoft.EntityFrameworkCore.Migrations;

namespace Mewdeko.Database.Migrations;

public partial class RemoveSnipesFromDb : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) 
        => migrationBuilder.DropTable("SnipeStore");
}