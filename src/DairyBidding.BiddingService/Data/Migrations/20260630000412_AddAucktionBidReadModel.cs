using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DairyBidding.BiddingService.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAucktionBidReadModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuctionBidReadModels",
                columns: table => new
                {
                    AuctionId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    HighestBidAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    HighestBidderId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TotalBids = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuctionBidReadModels", x => x.AuctionId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuctionBidReadModels");
        }
    }
}
