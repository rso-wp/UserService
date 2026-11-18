using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Infrastructure.Persistance.Migrations
{
    /// <inheritdoc />
    public partial class UseKeycloak : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_Email",
                schema: "user_service",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PasswordHash",
                schema: "user_service",
                table: "Users");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PasswordHash",
                schema: "user_service",
                table: "Users",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                schema: "user_service",
                table: "Users",
                column: "Email",
                unique: true,
                filter: "\"PasswordHash\" IS NOT NULL");
        }
    }
}
