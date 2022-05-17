using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CSVSeeder.API.Migrations
{
    public partial class AutoGuid : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE tt.TodoItems ADD DEFAULT NEWID() FOR Id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
