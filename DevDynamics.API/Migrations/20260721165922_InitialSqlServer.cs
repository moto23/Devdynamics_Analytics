using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevDynamics.API.Migrations
{
    /// <inheritdoc />
    public partial class InitialSqlServer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Contributors",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GitHubId = table.Column<long>(type: "bigint", nullable: false),
                    Login = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    AvatarUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    HtmlUrl = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Contributors", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DevActivities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Time = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Commits = table.Column<int>(type: "int", nullable: false),
                    PullRequests = table.Column<int>(type: "int", nullable: false),
                    Merges = table.Column<int>(type: "int", nullable: false),
                    Meetings = table.Column<int>(type: "int", nullable: false),
                    Documentation = table.Column<int>(type: "int", nullable: false),
                    Contributor = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Company = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    PROpenedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PRMergedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DevActivities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Repositories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GitHubId = table.Column<long>(type: "bigint", nullable: false),
                    Owner = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Language = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    HtmlUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DefaultBranch = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StarCount = table.Column<int>(type: "int", nullable: false),
                    LastSyncedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Repositories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Commits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Sha = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    RepositoryId = table.Column<int>(type: "int", nullable: false),
                    ContributorId = table.Column<int>(type: "int", nullable: true),
                    AuthorName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    AuthorEmail = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Message = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    CommittedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Commits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Commits_Contributors_ContributorId",
                        column: x => x.ContributorId,
                        principalTable: "Contributors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Commits_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PullRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GitHubId = table.Column<long>(type: "bigint", nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    RepositoryId = table.Column<int>(type: "int", nullable: false),
                    ContributorId = table.Column<int>(type: "int", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    State = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    MergedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClosedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PullRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PullRequests_Contributors_ContributorId",
                        column: x => x.ContributorId,
                        principalTable: "Contributors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PullRequests_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Commits_ContributorId_CommittedAt",
                table: "Commits",
                columns: new[] { "ContributorId", "CommittedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Commits_RepositoryId_CommittedAt",
                table: "Commits",
                columns: new[] { "RepositoryId", "CommittedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Commits_Sha",
                table: "Commits",
                column: "Sha",
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
                name: "IX_DevActivities_Company_Time",
                table: "DevActivities",
                columns: new[] { "Company", "Time" });

            migrationBuilder.CreateIndex(
                name: "IX_DevActivities_Contributor_Time",
                table: "DevActivities",
                columns: new[] { "Contributor", "Time" });

            migrationBuilder.CreateIndex(
                name: "IX_DevActivities_Time",
                table: "DevActivities",
                column: "Time");

            migrationBuilder.CreateIndex(
                name: "IX_PullRequests_ContributorId_CreatedAt",
                table: "PullRequests",
                columns: new[] { "ContributorId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PullRequests_GitHubId",
                table: "PullRequests",
                column: "GitHubId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PullRequests_RepositoryId_MergedAt",
                table: "PullRequests",
                columns: new[] { "RepositoryId", "MergedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PullRequests_RepositoryId_Number",
                table: "PullRequests",
                columns: new[] { "RepositoryId", "Number" },
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Commits");

            migrationBuilder.DropTable(
                name: "DevActivities");

            migrationBuilder.DropTable(
                name: "PullRequests");

            migrationBuilder.DropTable(
                name: "Contributors");

            migrationBuilder.DropTable(
                name: "Repositories");
        }
    }
}
