using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KenoKoch.Modules.Analytics.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "analytics");

            migrationBuilder.CreateTable(
                name: "DailyMetrics",
                schema: "analytics",
                columns: table => new
                {
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Metric = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Count = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyMetrics", x => new { x.Date, x.Metric });
                });

            migrationBuilder.CreateIndex(
                name: "IX_DailyMetrics_Metric",
                schema: "analytics",
                table: "DailyMetrics",
                column: "Metric");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DailyMetrics",
                schema: "analytics");
        }
    }
}
