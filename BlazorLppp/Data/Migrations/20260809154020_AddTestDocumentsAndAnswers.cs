using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlazorLppp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTestDocumentsAndAnswers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TestDocumentId",
                table: "TestAttempts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TestDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Instruction = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    OriginalFileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    FolderName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    RelativePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestDocuments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TestQuestions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TestDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Hint = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Type = table.Column<int>(type: "int", nullable: false),
                    ScaleMin = table.Column<int>(type: "int", nullable: true),
                    ScaleMax = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestQuestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TestQuestions_TestDocuments_TestDocumentId",
                        column: x => x.TestDocumentId,
                        principalTable: "TestDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TestOptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TestQuestionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Text = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestOptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TestOptions_TestQuestions_TestQuestionId",
                        column: x => x.TestQuestionId,
                        principalTable: "TestQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TestAnswers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TestAttemptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TestQuestionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SelectedOptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ScaleValue = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestAnswers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TestAnswers_TestAttempts_TestAttemptId",
                        column: x => x.TestAttemptId,
                        principalTable: "TestAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TestAnswers_TestOptions_SelectedOptionId",
                        column: x => x.SelectedOptionId,
                        principalTable: "TestOptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TestAnswers_TestQuestions_TestQuestionId",
                        column: x => x.TestQuestionId,
                        principalTable: "TestQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TestAttempts_TestDocumentId",
                table: "TestAttempts",
                column: "TestDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_TestAnswers_SelectedOptionId",
                table: "TestAnswers",
                column: "SelectedOptionId");

            migrationBuilder.CreateIndex(
                name: "IX_TestAnswers_TestAttemptId_TestQuestionId",
                table: "TestAnswers",
                columns: new[] { "TestAttemptId", "TestQuestionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TestAnswers_TestQuestionId",
                table: "TestAnswers",
                column: "TestQuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_TestDocuments_IsActive",
                table: "TestDocuments",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_TestDocuments_RelativePath",
                table: "TestDocuments",
                column: "RelativePath",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TestOptions_TestQuestionId_SortOrder",
                table: "TestOptions",
                columns: new[] { "TestQuestionId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_TestQuestions_TestDocumentId_SortOrder",
                table: "TestQuestions",
                columns: new[] { "TestDocumentId", "SortOrder" });

            migrationBuilder.AddForeignKey(
                name: "FK_TestAttempts_TestDocuments_TestDocumentId",
                table: "TestAttempts",
                column: "TestDocumentId",
                principalTable: "TestDocuments",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TestAttempts_TestDocuments_TestDocumentId",
                table: "TestAttempts");

            migrationBuilder.DropTable(
                name: "TestAnswers");

            migrationBuilder.DropTable(
                name: "TestOptions");

            migrationBuilder.DropTable(
                name: "TestQuestions");

            migrationBuilder.DropTable(
                name: "TestDocuments");

            migrationBuilder.DropIndex(
                name: "IX_TestAttempts_TestDocumentId",
                table: "TestAttempts");

            migrationBuilder.DropColumn(
                name: "TestDocumentId",
                table: "TestAttempts");
        }
    }
}
