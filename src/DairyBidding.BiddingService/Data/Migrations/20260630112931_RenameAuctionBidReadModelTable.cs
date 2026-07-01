using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DairyBidding.BiddingService.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenameAuctionBidReadModelTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_AuctionBidReadModels",
                table: "AuctionBidReadModels");

            migrationBuilder.RenameTable(
                name: "AuctionBidReadModels",
                newName: "auction_bid_read_models");

            migrationBuilder.AddPrimaryKey(
                name: "PK_auction_bid_read_models",
                table: "auction_bid_read_models",
                column: "AuctionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_auction_bid_read_models",
                table: "auction_bid_read_models");

            migrationBuilder.RenameTable(
                name: "auction_bid_read_models",
                newName: "AuctionBidReadModels");

            migrationBuilder.AddPrimaryKey(
                name: "PK_AuctionBidReadModels",
                table: "AuctionBidReadModels",
                column: "AuctionId");
        }
    }
}
