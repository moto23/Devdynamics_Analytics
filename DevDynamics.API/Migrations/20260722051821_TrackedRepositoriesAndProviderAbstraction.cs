using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevDynamics.API.Migrations
{
    /// <inheritdoc />
    public partial class TrackedRepositoriesAndProviderAbstraction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Commits_Repositories_RepositoryId",
                table: "Commits");

            migrationBuilder.DropForeignKey(
                name: "FK_PullRequests_Repositories_RepositoryId",
                table: "PullRequests");

            migrationBuilder.DropTable(
                name: "Repositories");

            migrationBuilder.DropIndex(
                name: "IX_PullRequests_GitHubId",
                table: "PullRequests");

            migrationBuilder.DropIndex(
                name: "IX_Contributors_GitHubId",
                table: "Contributors");

            migrationBuilder.DropIndex(
                name: "IX_Contributors_Login",
                table: "Contributors");

            migrationBuilder.DropIndex(
                name: "IX_Commits_Sha",
                table: "Commits");

            migrationBuilder.DropColumn(
                name: "GitHubId",
                table: "PullRequests");

            migrationBuilder.DropColumn(
                name: "GitHubId",
                table: "Contributors");

            migrationBuilder.AddColumn<string>(
                name: "AuthorLogin",
                table: "PullRequests",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "PullRequests",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "PullRequests",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AlterColumn<string>(
                name: "HtmlUrl",
                table: "Contributors",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "AvatarUrl",
                table: "Contributors",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "Contributors",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsBot",
                table: "Contributors",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Provider",
                table: "Contributors",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AlterColumn<string>(
                name: "Sha",
                table: "Commits",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(40)",
                oldMaxLength: 40);

            migrationBuilder.CreateTable(
                name: "TrackedRepositories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Provider = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ExternalId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Owner = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Language = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    HtmlUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DefaultBranch = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    StarCount = table.Column<int>(type: "int", nullable: false),
                    IsPrivate = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsDemoData = table.Column<bool>(type: "bit", nullable: false),
                    AddedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AddedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SyncStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LastSyncStartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastSyncCompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastSyncError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    LastSyncDurationMs = table.Column<int>(type: "int", nullable: true),
                    CommitsSyncedThroughUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PullsSyncedThroughUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CommitsSyncToken = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PullsSyncToken = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    TotalCommits = table.Column<int>(type: "int", nullable: false),
                    TotalPullRequests = table.Column<int>(type: "int", nullable: false),
                    TotalContributors = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackedRepositories", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PullRequests_RepositoryId_UpdatedAt",
                table: "PullRequests",
                columns: new[] { "RepositoryId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Contributors_Login",
                table: "Contributors",
                column: "Login");

            migrationBuilder.CreateIndex(
                name: "IX_Contributors_Provider_ExternalId",
                table: "Contributors",
                columns: new[] { "Provider", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Commits_RepositoryId_Sha",
                table: "Commits",
                columns: new[] { "RepositoryId", "Sha" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrackedRepositories_IsActive_SyncStatus",
                table: "TrackedRepositories",
                columns: new[] { "IsActive", "SyncStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_TrackedRepositories_Provider_ExternalId",
                table: "TrackedRepositories",
                columns: new[] { "Provider", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrackedRepositories_Provider_FullName",
                table: "TrackedRepositories",
                columns: new[] { "Provider", "FullName" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Commits_TrackedRepositories_RepositoryId",
                table: "Commits",
                column: "RepositoryId",
                principalTable: "TrackedRepositories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PullRequests_TrackedRepositories_RepositoryId",
                table: "PullRequests",
                column: "RepositoryId",
                principalTable: "TrackedRepositories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Commits_TrackedRepositories_RepositoryId",
                table: "Commits");

            migrationBuilder.DropForeignKey(
                name: "FK_PullRequests_TrackedRepositories_RepositoryId",
                table: "PullRequests");

            migrationBuilder.DropTable(
                name: "TrackedRepositories");

            migrationBuilder.DropIndex(
                name: "IX_PullRequests_RepositoryId_UpdatedAt",
                table: "PullRequests");

            migrationBuilder.DropIndex(
                name: "IX_Contributors_Login",
                table: "Contributors");

            migrationBuilder.DropIndex(
                name: "IX_Contributors_Provider_ExternalId",
                table: "Contributors");

            migrationBuilder.DropIndex(
                name: "IX_Commits_RepositoryId_Sha",
                table: "Commits");

            migrationBuilder.DropColumn(
                name: "AuthorLogin",
                table: "PullRequests");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "PullRequests");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "PullRequests");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "Contributors");

            migrationBuilder.DropColumn(
                name: "IsBot",
                table: "Contributors");

            migrationBuilder.DropColumn(
                name: "Provider",
                table: "Contributors");

            migrationBuilder.AddColumn<long>(
                name: "GitHubId",
                table: "PullRequests",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AlterColumn<string>(
                name: "HtmlUrl",
                table: "Contributors",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "AvatarUrl",
                table: "Contributors",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.AddColumn<long>(
                name: "GitHubId",
                table: "Contributors",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AlterColumn<string>(
                name: "Sha",
                table: "Commits",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(64)",
                oldMaxLength: 64);

            migrationBuilder.CreateTable(
                name: "Repositories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DefaultBranch = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FullName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    GitHubId = table.Column<long>(type: "bigint", nullable: false),
                    HtmlUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Language = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    LastSyncedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Owner = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    StarCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Repositories", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PullRequests_GitHubId",
                table: "PullRequests",
                column: "GitHubId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Contributors_GitHubId",
                table: "Contributors",
                column: "GitHubId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Contributors_Login",
                table: "Contributors",
                column: "Login",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Commits_Sha",
                table: "Commits",
                column: "Sha",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Repositories_FullName",
                table: "Repositories",
                column: "FullName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Repositories_GitHubId",
                table: "Repositories",
                column: "GitHubId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Commits_Repositories_RepositoryId",
                table: "Commits",
                column: "RepositoryId",
                principalTable: "Repositories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PullRequests_Repositories_RepositoryId",
                table: "PullRequests",
                column: "RepositoryId",
                principalTable: "Repositories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
