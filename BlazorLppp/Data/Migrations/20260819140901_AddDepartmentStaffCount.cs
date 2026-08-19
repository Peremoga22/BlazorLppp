using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlazorLppp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDepartmentStaffCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "StaffCount",
                table: "Departments",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StaffCount",
                table: "Departments");
        }
    }
}
