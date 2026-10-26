using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finance_API.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOccurrenceImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "Occurrences",
                type: "TEXT",
                maxLength: 250,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImportId",
                table: "Occurrences",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Occurrences_AccountId_ImportId",
                table: "Occurrences",
                columns: new[] { "AccountId", "ImportId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Occurrences_AccountId_ImportId",
                table: "Occurrences");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "Occurrences");

            migrationBuilder.DropColumn(
                name: "ImportId",
                table: "Occurrences");
        }
    }
}
