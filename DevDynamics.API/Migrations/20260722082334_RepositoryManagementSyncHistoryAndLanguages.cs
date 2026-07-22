using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevDynamics.API.Migrations
{
    /// <inheritdoc />
    public partial class RepositoryManagementSyncHistoryAndLanguages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ForkCount",
                table: "TrackedRepositories",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsPinned",
                table: "TrackedRepositories",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Nickname",
                table: "TrackedRepositories",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "TrackedRepositories",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OpenIssueCount",
                table: "TrackedRepositories",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SyncIntervalMinutes",
                table: "TrackedRepositories",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RepositoryLanguages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RepositoryId = table.Column<int>(type: "int", nullable: false),
                    Language = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Bytes = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RepositoryLanguages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RepositoryLanguages_TrackedRepositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "TrackedRepositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SyncRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RepositoryId = table.Column<int>(type: "int", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CommitsIngested = table.Column<int>(type: "int", nullable: false),
                    PullRequestsIngested = table.Column<int>(type: "int", nullable: false),
                    ContributorsAdded = table.Column<int>(type: "int", nullable: false),
                    Truncated = table.Column<bool>(type: "bit", nullable: false),
                    DurationMs = table.Column<int>(type: "int", nullable: false),
                    Error = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Trigger = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SyncRuns_TrackedRepositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "TrackedRepositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TrackedRepositories_IsActive_LastSyncCompletedAtUtc",
                table: "TrackedRepositories",
                columns: new[] { "IsActive", "LastSyncCompletedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RepositoryLanguages_RepositoryId_Language",
                table: "RepositoryLanguages",
                columns: new[] { "RepositoryId", "Language" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SyncRuns_RepositoryId_StartedAtUtc",
                table: "SyncRuns",
                columns: new[] { "RepositoryId", "StartedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RepositoryLanguages");

            migrationBuilder.DropTable(
                name: "SyncRuns");

            migrationBuilder.DropIndex(
                name: "IX_TrackedRepositories_IsActive_LastSyncCompletedAtUtc",
                table: "TrackedRepositories");

            migrationBuilder.DropColumn(
                name: "ForkCount",
                table: "TrackedRepositories");

            migrationBuilder.DropColumn(
                name: "IsPinned",
                table: "TrackedRepositories");

            migrationBuilder.DropColumn(
                name: "Nickname",
                table: "TrackedRepositories");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "TrackedRepositories");

            migrationBuilder.DropColumn(
                name: "OpenIssueCount",
                table: "TrackedRepositories");

            migrationBuilder.DropColumn(
                name: "SyncIntervalMinutes",
                table: "TrackedRepositories");
        }
    }
}
