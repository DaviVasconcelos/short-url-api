using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShortUrl.Infrastructure.Postgres.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExpiresAtIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_ShortUrls_ExpiresAt",
                table: "ShortUrls",
                column: "ExpiresAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ShortUrls_ExpiresAt",
                table: "ShortUrls");
        }
    }
}
