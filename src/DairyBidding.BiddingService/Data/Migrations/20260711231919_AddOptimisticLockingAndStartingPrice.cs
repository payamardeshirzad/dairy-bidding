using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DairyBidding.BiddingService.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOptimisticLockingAndStartingPrice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "starting_price",
                table: "auction_read_models",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AlterColumn<decimal>(
                name: "highest_bid_amount",
                table: "auction_bid_read_models",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.AddColumn<int>(
                name: "row_version",
                table: "auction_bid_read_models",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "starting_price",
                table: "auction_read_models");

            migrationBuilder.DropColumn(
                name: "row_version",
                table: "auction_bid_read_models");

            migrationBuilder.AlterColumn<decimal>(
                name: "highest_bid_amount",
                table: "auction_bid_read_models",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,4)",
                oldPrecision: 18,
                oldScale: 4);
        }
    }
}
