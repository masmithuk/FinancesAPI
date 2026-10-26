using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finance_API.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBalanceNote : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Note",
                table: "Balances",
                type: "TEXT",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Note",
                table: "Balances");
        }
    }
}
