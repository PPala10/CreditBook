using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CreditBook.Storage.Migrations
{
    /// <inheritdoc />
    public partial class LedgerTools : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReversalReason",
                table: "Payments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DueDate",
                table: "DebtRecords",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "VoidReason",
                table: "DebtRecords",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "VoidedAt",
                table: "DebtRecords",
                type: "TEXT",
                nullable: true);

            migrationBuilder.Sql("UPDATE DebtRecords SET DueDate = datetime(Date, '+30 days')");

            migrationBuilder.CreateTable(
                name: "DueDateChanges",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DebtRecordId = table.Column<int>(type: "INTEGER", nullable: false),
                    OldDueDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    NewDueDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ChangedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DueDateChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DueDateChanges_DebtRecords_DebtRecordId",
                        column: x => x.DebtRecordId,
                        principalTable: "DebtRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DueDateChanges_DebtRecordId",
                table: "DueDateChanges",
                column: "DebtRecordId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DueDateChanges");

            migrationBuilder.DropColumn(
                name: "ReversalReason",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "DueDate",
                table: "DebtRecords");

            migrationBuilder.DropColumn(
                name: "VoidReason",
                table: "DebtRecords");

            migrationBuilder.DropColumn(
                name: "VoidedAt",
                table: "DebtRecords");
        }
    }
}
