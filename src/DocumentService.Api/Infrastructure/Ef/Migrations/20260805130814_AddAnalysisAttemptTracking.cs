using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocumentService.Api.Infrastructure.Ef.Migrations
{
    /// <inheritdoc />
    public partial class AddAnalysisAttemptTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Documents_Status",
                table: "Documents");

            migrationBuilder.AddColumn<int>(
                name: "AnalysisAttempts",
                table: "Documents",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AnalysisStartedAtUtc",
                table: "Documents",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FailureReason",
                table: "Documents",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            // Documents already Analyzing when this ran have no start time, and the sweep
            // ignores rows whose start time is null - so without this they would be the one
            // population permanently invisible to the thing built to find them.
            //
            // Stamped with "now" rather than a guessed original: it costs one sweep window
            // of delay and cannot mass-requeue a backlog the instant this deploys. Status
            // is stored as a string, hence the literal.
            migrationBuilder.Sql(@"
                UPDATE [Documents]
                SET [AnalysisStartedAtUtc] = SYSDATETIMEOFFSET(),
                    [AnalysisAttempts] = 1
                WHERE [Status] = 'Analyzing';");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_Status_AnalysisStartedAtUtc",
                table: "Documents",
                columns: new[] { "Status", "AnalysisStartedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Documents_Status_AnalysisStartedAtUtc",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "AnalysisAttempts",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "AnalysisStartedAtUtc",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "FailureReason",
                table: "Documents");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_Status",
                table: "Documents",
                column: "Status");
        }
    }
}
