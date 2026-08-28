using BlazorLppp.Data;

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlazorLppp.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260828153000_AddTestConstructor")]
    public partial class AddTestConstructor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsManual",
                table: "TestDocuments",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "AnswerStyle",
                table: "TestQuestions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_TestDocuments_IsManual",
                table: "TestDocuments",
                column: "IsManual");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TestDocuments_IsManual",
                table: "TestDocuments");

            migrationBuilder.DropColumn(
                name: "IsManual",
                table: "TestDocuments");

            migrationBuilder.DropColumn(
                name: "AnswerStyle",
                table: "TestQuestions");
        }
    }
}
