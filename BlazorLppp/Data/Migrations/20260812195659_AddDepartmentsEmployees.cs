using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlazorLppp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDepartmentsEmployees : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "EmployeeId",
                table: "TestAttempts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Departments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Departments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TestScaleResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TestAttemptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScaleCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    RawScore = table.Column<int>(type: "int", nullable: false),
                    StandardScore = table.Column<int>(type: "int", nullable: true),
                    Interpretation = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestScaleResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TestScaleResults_TestAttempts_TestAttemptId",
                        column: x => x.TestAttemptId,
                        principalTable: "TestAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Employees",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    MiddleName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Employees", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Employees_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TestAttempts_EmployeeId",
                table: "TestAttempts",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_Departments_Name",
                table: "Departments",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Departments_Number",
                table: "Departments",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Employees_DepartmentId_LastName_FirstName_MiddleName",
                table: "Employees",
                columns: new[] { "DepartmentId", "LastName", "FirstName", "MiddleName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TestScaleResults_TestAttemptId_ScaleCode",
                table: "TestScaleResults",
                columns: new[] { "TestAttemptId", "ScaleCode" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_TestAttempts_Employees_EmployeeId",
                table: "TestAttempts",
                column: "EmployeeId",
                principalTable: "Employees",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TestAttempts_Employees_EmployeeId",
                table: "TestAttempts");

            migrationBuilder.DropTable(
                name: "Employees");

            migrationBuilder.DropTable(
                name: "TestScaleResults");

            migrationBuilder.DropTable(
                name: "Departments");

            migrationBuilder.DropIndex(
                name: "IX_TestAttempts_EmployeeId",
                table: "TestAttempts");

            migrationBuilder.DropColumn(
                name: "EmployeeId",
                table: "TestAttempts");
        }
    }
}
