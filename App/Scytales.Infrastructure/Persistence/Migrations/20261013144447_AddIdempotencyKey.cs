using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scytales.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIdempotencyKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "idempotency_key",
                table: "applications",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "idempotency_request_hash",
                table: "applications",
                type: "character(64)",
                fixedLength: true,
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_applications_idempotency_key",
                table: "applications",
                column: "idempotency_key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_applications_idempotency_key",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "idempotency_key",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "idempotency_request_hash",
                table: "applications");
        }
    }
}
