using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DairyBidding.BiddingService.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProcessedMessageUniqueIndexAndSnakeCase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_processed_messages",
                table: "processed_messages");

            migrationBuilder.DropPrimaryKey(
                name: "PK_bids",
                table: "bids");

            migrationBuilder.DropPrimaryKey(
                name: "PK_auction_read_models",
                table: "auction_read_models");

            migrationBuilder.DropPrimaryKey(
                name: "PK_auction_bid_read_models",
                table: "auction_bid_read_models");

            migrationBuilder.RenameColumn(
                name: "processedatutc",
                table: "processed_messages",
                newName: "processed_at_utc");

            migrationBuilder.RenameColumn(
                name: "messageid",
                table: "processed_messages",
                newName: "message_id");

            migrationBuilder.RenameColumn(
                name: "idempotencykey",
                table: "bids",
                newName: "idempotency_key");

            migrationBuilder.RenameColumn(
                name: "createdatutc",
                table: "bids",
                newName: "created_at_utc");

            migrationBuilder.RenameColumn(
                name: "bidderid",
                table: "bids",
                newName: "bidder_id");

            migrationBuilder.RenameColumn(
                name: "auctionid",
                table: "bids",
                newName: "auction_id");

            migrationBuilder.RenameIndex(
                name: "IX_bids_bidderid_idempotencykey",
                table: "bids",
                newName: "ix_bids_bidder_id_idempotency_key");

            migrationBuilder.RenameColumn(
                name: "updatedatutc",
                table: "auction_read_models",
                newName: "updated_at_utc");

            migrationBuilder.RenameColumn(
                name: "startsat",
                table: "auction_read_models",
                newName: "starts_at");

            migrationBuilder.RenameColumn(
                name: "endsat",
                table: "auction_read_models",
                newName: "ends_at");

            migrationBuilder.RenameColumn(
                name: "auctionid",
                table: "auction_read_models",
                newName: "auction_id");

            migrationBuilder.RenameColumn(
                name: "UpdatedAtUtc",
                table: "auction_bid_read_models",
                newName: "updated_at_utc");

            migrationBuilder.RenameColumn(
                name: "TotalBids",
                table: "auction_bid_read_models",
                newName: "total_bids");

            migrationBuilder.RenameColumn(
                name: "HighestBidderId",
                table: "auction_bid_read_models",
                newName: "highest_bidder_id");

            migrationBuilder.RenameColumn(
                name: "HighestBidAmount",
                table: "auction_bid_read_models",
                newName: "highest_bid_amount");

            migrationBuilder.RenameColumn(
                name: "AuctionId",
                table: "auction_bid_read_models",
                newName: "auction_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_processed_messages",
                table: "processed_messages",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_bids",
                table: "bids",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_auction_read_models",
                table: "auction_read_models",
                column: "auction_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_auction_bid_read_models",
                table: "auction_bid_read_models",
                column: "auction_id");

            migrationBuilder.CreateIndex(
                name: "ix_processed_messages_message_id",
                table: "processed_messages",
                column: "message_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_processed_messages",
                table: "processed_messages");

            migrationBuilder.DropIndex(
                name: "ix_processed_messages_message_id",
                table: "processed_messages");

            migrationBuilder.DropPrimaryKey(
                name: "pk_bids",
                table: "bids");

            migrationBuilder.DropPrimaryKey(
                name: "pk_auction_read_models",
                table: "auction_read_models");

            migrationBuilder.DropPrimaryKey(
                name: "pk_auction_bid_read_models",
                table: "auction_bid_read_models");

            migrationBuilder.RenameColumn(
                name: "processed_at_utc",
                table: "processed_messages",
                newName: "processedatutc");

            migrationBuilder.RenameColumn(
                name: "message_id",
                table: "processed_messages",
                newName: "messageid");

            migrationBuilder.RenameColumn(
                name: "idempotency_key",
                table: "bids",
                newName: "idempotencykey");

            migrationBuilder.RenameColumn(
                name: "created_at_utc",
                table: "bids",
                newName: "createdatutc");

            migrationBuilder.RenameColumn(
                name: "bidder_id",
                table: "bids",
                newName: "bidderid");

            migrationBuilder.RenameColumn(
                name: "auction_id",
                table: "bids",
                newName: "auctionid");

            migrationBuilder.RenameIndex(
                name: "ix_bids_bidder_id_idempotency_key",
                table: "bids",
                newName: "IX_bids_bidderid_idempotencykey");

            migrationBuilder.RenameColumn(
                name: "updated_at_utc",
                table: "auction_read_models",
                newName: "updatedatutc");

            migrationBuilder.RenameColumn(
                name: "starts_at",
                table: "auction_read_models",
                newName: "startsat");

            migrationBuilder.RenameColumn(
                name: "ends_at",
                table: "auction_read_models",
                newName: "endsat");

            migrationBuilder.RenameColumn(
                name: "auction_id",
                table: "auction_read_models",
                newName: "auctionid");

            migrationBuilder.RenameColumn(
                name: "updated_at_utc",
                table: "auction_bid_read_models",
                newName: "UpdatedAtUtc");

            migrationBuilder.RenameColumn(
                name: "total_bids",
                table: "auction_bid_read_models",
                newName: "TotalBids");

            migrationBuilder.RenameColumn(
                name: "highest_bidder_id",
                table: "auction_bid_read_models",
                newName: "HighestBidderId");

            migrationBuilder.RenameColumn(
                name: "highest_bid_amount",
                table: "auction_bid_read_models",
                newName: "HighestBidAmount");

            migrationBuilder.RenameColumn(
                name: "auction_id",
                table: "auction_bid_read_models",
                newName: "AuctionId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_processed_messages",
                table: "processed_messages",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_bids",
                table: "bids",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_auction_read_models",
                table: "auction_read_models",
                column: "auctionid");

            migrationBuilder.AddPrimaryKey(
                name: "PK_auction_bid_read_models",
                table: "auction_bid_read_models",
                column: "AuctionId");
        }
    }
}
