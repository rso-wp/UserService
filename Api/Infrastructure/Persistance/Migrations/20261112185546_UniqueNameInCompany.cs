using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Infrastructure.Persistance.Migrations
{
    /// <inheritdoc />
    public partial class UniqueNameInCompany : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Users_Company_Name_Lastname",
                schema: "user_service",
                table: "Users",
                columns: new[] { "Company", "Name", "Lastname" },
                unique: true,
                filter: "\"Company\" IS NOT NULL AND \"Name\" IS NOT NULL AND \"Lastname\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_Company_Name_Lastname",
                schema: "user_service",
                table: "Users");
        }
    }
}
