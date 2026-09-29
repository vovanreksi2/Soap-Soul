using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SoapAndSoul.Data.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class IngredientInStock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "InStock",
                table: "Ingredients",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // Supplier-catalog rows stay reference only; what the user created (no photo or an uploaded one)
            // or already uses in a recipe is in stock.
            migrationBuilder.Sql(
                "UPDATE [Ingredients] SET [InStock] = 1 WHERE [PhotoUrl] IS NULL OR [PhotoUrl] LIKE '/images/%' OR [Id] IN (SELECT [IngredientId] FROM [RecipeItems])");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InStock",
                table: "Ingredients");
        }
    }
}
