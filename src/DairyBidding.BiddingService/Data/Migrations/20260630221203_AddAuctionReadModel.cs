using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DairyBidding.BiddingService.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAuctionReadModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "auction_read_models",
                columns: table => new
                {
                    auctionid = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    startsat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    endsat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updatedatutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_auction_read_models", x => x.auctionid);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "auction_read_models");
        }
    }
}
