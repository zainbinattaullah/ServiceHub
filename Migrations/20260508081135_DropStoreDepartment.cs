using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceHub.Migrations
{
    /// <inheritdoc />
    public partial class DropStoreDepartment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Department",
                table: "Stores");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Department",
                table: "Stores",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);
        }
    }
}
