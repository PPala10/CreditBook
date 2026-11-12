using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CreditBook.UI.Migrations
{
    /// <inheritdoc />
    public partial class AddOverdueNotificationFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "OverdueNotificationSent",
                table: "DebtRecords",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OverdueNotificationSent",
                table: "DebtRecords");
        }
    }
}
