using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KenoKoch.Modules.Guestbook.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "guestbook");

            migrationBuilder.CreateTable(
                name: "Entries",
                schema: "guestbook",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Company = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    Role = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsApproved = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Entries", x => x.Id);
                });

            migrationBuilder.InsertData(
                schema: "guestbook",
                table: "Entries",
                columns: new[] { "Id", "Company", "CreatedAt", "IsApproved", "Message", "Name", "Role" },
                values: new object[] { 1, null, new DateTimeOffset(new DateTime(2026, 10, 24, 12, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Willkommen in meinem Gästebuch! Jeder Eintrag wird mit Entity Framework Core in Azure SQL gespeichert – das erzeugte SQL sehen Sie direkt unter dem Formular. Ich freue mich über Ihren Gruß.", "Keno Koch", "Developer" });

            migrationBuilder.CreateIndex(
                name: "IX_Entries_IsApproved_CreatedAt",
                schema: "guestbook",
                table: "Entries",
                columns: new[] { "IsApproved", "CreatedAt" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Entries",
                schema: "guestbook");
        }
    }
}
