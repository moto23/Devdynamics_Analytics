using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevDynamics.API.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DevActivities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Time = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Commits = table.Column<int>(type: "INTEGER", nullable: false),
                    PullRequests = table.Column<int>(type: "INTEGER", nullable: false),
                    Merges = table.Column<int>(type: "INTEGER", nullable: false),
                    Meetings = table.Column<int>(type: "INTEGER", nullable: false),
                    Documentation = table.Column<int>(type: "INTEGER", nullable: false),
                    Contributor = table.Column<string>(type: "TEXT", nullable: false),
                    PROpenedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PRMergedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DevActivities", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DevActivities");
        }
    }
}
