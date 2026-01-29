using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Petit_shope_Asp_backend.Migrations
{
    /// <inheritdoc />
    public partial class AddPayPalFieldsToOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add PayPal fields to Orders table
            migrationBuilder.AddColumn<string>(
                name: "PayPalOrderId",
                table: "Orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayPalPayerId",
                table: "Orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayPalPaymentStatus",
                table: "Orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayPalCaptureId",
                table: "Orders",
                type: "text",
                nullable: true);

            // Add PayPalMerchantId to Users table
            migrationBuilder.AddColumn<string>(
                name: "PayPalMerchantId",
                table: "Users",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Remove PayPal fields from Orders table
            migrationBuilder.DropColumn(
                name: "PayPalOrderId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PayPalPayerId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PayPalPaymentStatus",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PayPalCaptureId",
                table: "Orders");

            // Remove PayPalMerchantId from Users table
            migrationBuilder.DropColumn(
                name: "PayPalMerchantId",
                table: "Users");
        }
    }
}
