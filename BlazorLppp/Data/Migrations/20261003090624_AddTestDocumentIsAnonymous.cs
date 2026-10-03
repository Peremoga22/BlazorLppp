using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlazorLppp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTestDocumentIsAnonymous : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsAnonymous",
                table: "TestDocuments",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsAnonymous",
                table: "TestDocuments");
        }
    }
}
