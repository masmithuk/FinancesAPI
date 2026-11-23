using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finance_API.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountBalanceCheckedOn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "BalanceCheckedOn",
                table: "Accounts",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BalanceCheckedOn",
                table: "Accounts");
        }
    }
}
