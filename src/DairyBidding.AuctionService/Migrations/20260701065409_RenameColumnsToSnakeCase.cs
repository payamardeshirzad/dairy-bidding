using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DairyBidding.AuctionService.Migrations
{
    /// <inheritdoc />
    public partial class RenameColumnsToSnakeCase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_auctions",
                table: "auctions");

            migrationBuilder.RenameColumn(
                name: "startsat",
                table: "auctions",
                newName: "starts_at");

            migrationBuilder.RenameColumn(
                name: "startingprice",
                table: "auctions",
                newName: "starting_price");

            migrationBuilder.RenameColumn(
                name: "endsat",
                table: "auctions",
                newName: "ends_at");

            migrationBuilder.RenameColumn(
                name: "createdatutc",
                table: "auctions",
                newName: "created_at_utc");

            migrationBuilder.AddPrimaryKey(
                name: "pk_auctions",
                table: "auctions",
                column: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_auctions",
                table: "auctions");

            migrationBuilder.RenameColumn(
                name: "starts_at",
                table: "auctions",
                newName: "startsat");

            migrationBuilder.RenameColumn(
                name: "starting_price",
                table: "auctions",
                newName: "startingprice");

            migrationBuilder.RenameColumn(
                name: "ends_at",
                table: "auctions",
                newName: "endsat");

            migrationBuilder.RenameColumn(
                name: "created_at_utc",
                table: "auctions",
                newName: "createdatutc");

            migrationBuilder.AddPrimaryKey(
                name: "PK_auctions",
                table: "auctions",
                column: "id");
        }
    }
}
