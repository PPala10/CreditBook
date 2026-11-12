using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CreditBook.Storage.Migrations
{
    /// <inheritdoc />
    public partial class PaymentReversal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ReversedAt",
                table: "Payments",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReversedAt",
                table: "Payments");
        }
    }
}
