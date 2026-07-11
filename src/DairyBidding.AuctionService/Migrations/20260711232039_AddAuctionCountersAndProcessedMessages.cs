using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DairyBidding.AuctionService.Migrations
{
    /// <inheritdoc />
    public partial class AddAuctionCountersAndProcessedMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "starting_price",
                table: "auctions",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)");

            migrationBuilder.AddColumn<int>(
                name: "bid_count",
                table: "auctions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "current_price",
                table: "auctions",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "row_version",
                table: "auctions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "processed_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    message_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    processed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_processed_messages", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_inbox_state_delivered",
                table: "inbox_state",
                column: "delivered");

            migrationBuilder.CreateIndex(
                name: "ix_processed_messages_message_id",
                table: "processed_messages",
                column: "message_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "processed_messages");

            migrationBuilder.DropIndex(
                name: "ix_inbox_state_delivered",
                table: "inbox_state");

            migrationBuilder.DropColumn(
                name: "bid_count",
                table: "auctions");

            migrationBuilder.DropColumn(
                name: "current_price",
                table: "auctions");

            migrationBuilder.DropColumn(
                name: "row_version",
                table: "auctions");

            migrationBuilder.AlterColumn<decimal>(
                name: "starting_price",
                table: "auctions",
                type: "numeric(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,4)",
                oldPrecision: 18,
                oldScale: 4);
        }
    }
}
