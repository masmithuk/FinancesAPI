using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finance_API.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(FinanceDbContext))]
    [Migration("20261026200000_ScopeOccurrenceReferenceToAccount")]
    public partial class ScopeOccurrenceReferenceToAccount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Occurrences_ExternalReference",
                table: "Occurrences");

            migrationBuilder.CreateIndex(
                name: "IX_Occurrences_AccountId_ExternalReference",
                table: "Occurrences",
                columns: new[] { "AccountId", "ExternalReference" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Occurrences_AccountId_ExternalReference",
                table: "Occurrences");

            migrationBuilder.CreateIndex(
                name: "IX_Occurrences_ExternalReference",
                table: "Occurrences",
                column: "ExternalReference",
                unique: true);
        }
    }
}
