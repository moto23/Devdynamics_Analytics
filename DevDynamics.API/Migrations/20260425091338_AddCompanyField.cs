using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevDynamics.API.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyField : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Company",
                table: "DevActivities",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Company",
                table: "DevActivities");
        }
    }
}
