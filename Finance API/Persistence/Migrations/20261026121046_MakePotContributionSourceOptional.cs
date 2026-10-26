using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finance_API.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MakePotContributionSourceOptional : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PotContributions_Accounts_AccountId",
                table: "PotContributions");

            migrationBuilder.DropForeignKey(
                name: "FK_PotContributions_Balances_BalanceId",
                table: "PotContributions");

            migrationBuilder.AlterColumn<Guid>(
                name: "BalanceId",
                table: "PotContributions",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<Guid>(
                name: "AccountId",
                table: "PotContributions",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT");

            migrationBuilder.AddForeignKey(
                name: "FK_PotContributions_Accounts_AccountId",
                table: "PotContributions",
                column: "AccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_PotContributions_Balances_BalanceId",
                table: "PotContributions",
                column: "BalanceId",
                principalTable: "Balances",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PotContributions_Accounts_AccountId",
                table: "PotContributions");

            migrationBuilder.DropForeignKey(
                name: "FK_PotContributions_Balances_BalanceId",
                table: "PotContributions");

            migrationBuilder.AlterColumn<Guid>(
                name: "BalanceId",
                table: "PotContributions",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "AccountId",
                table: "PotContributions",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_PotContributions_Accounts_AccountId",
                table: "PotContributions",
                column: "AccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PotContributions_Balances_BalanceId",
                table: "PotContributions",
                column: "BalanceId",
                principalTable: "Balances",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
