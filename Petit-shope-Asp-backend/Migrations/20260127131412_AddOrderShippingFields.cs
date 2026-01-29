using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Petit_shope_Asp_backend.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderShippingFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ShippingAddress",
                table: "Orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShippingType",
                table: "Orders",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ShippingAddress",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ShippingType",
                table: "Orders");
        }
    }
}
