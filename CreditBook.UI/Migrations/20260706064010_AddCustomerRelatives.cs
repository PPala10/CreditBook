using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CreditBook.UI.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerRelatives : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomerRelatives",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CustomerId = table.Column<int>(type: "INTEGER", nullable: false),
                    RelativeId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerRelatives", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerRelatives_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerRelatives_Customers_RelativeId",
                        column: x => x.RelativeId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerRelatives_CustomerId",
                table: "CustomerRelatives",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerRelatives_RelativeId",
                table: "CustomerRelatives",
                column: "RelativeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomerRelatives");
        }
    }
}
