using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlazorLppp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAttemptResultFile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ResultFileName",
                table: "TestAttempts",
                type: "nvarchar(260)",
                maxLength: 260,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResultRelativePath",
                table: "TestAttempts",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ResultFileName",
                table: "TestAttempts");

            migrationBuilder.DropColumn(
                name: "ResultRelativePath",
                table: "TestAttempts");
        }
    }
}
