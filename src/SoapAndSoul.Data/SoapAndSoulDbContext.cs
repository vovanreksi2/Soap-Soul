using Microsoft.EntityFrameworkCore;
using SoapAndSoul.Data.Entities;

namespace SoapAndSoul.Data;

public class SoapAndSoulDbContext(DbContextOptions<SoapAndSoulDbContext> options) : DbContext(options)
{
    public DbSet<Ingredient> Ingredients => Set<Ingredient>();
    public DbSet<Recipe> Recipes => Set<Recipe>();
    public DbSet<RecipeItem> RecipeItems => Set<RecipeItem>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        builder.Properties<decimal>().HavePrecision(18, 4);
        builder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(32);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Ingredient>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.PhotoUrl).HasMaxLength(500);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.Line, x.Category });
            e.HasQueryFilter(x => !x.IsDeleted);
        });

        b.Entity<Recipe>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Description).HasMaxLength(4000);
            e.Property(x => x.PhotoUrl).HasMaxLength(500);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => x.Line);
            e.HasQueryFilter(x => !x.IsDeleted);
            e.HasMany(x => x.Items).WithOne(x => x.Recipe).HasForeignKey(x => x.RecipeId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<RecipeItem>(e =>
        {
            e.HasKey(x => new { x.RecipeId, x.IngredientId });
            e.HasOne(x => x.Ingredient).WithMany().HasForeignKey(x => x.IngredientId).OnDelete(DeleteBehavior.Restrict);
            // Items of a deleted ingredient are removed on delete, so this filter only mirrors the parent's.
            e.HasQueryFilter(x => !x.Ingredient.IsDeleted);
        });
    }
}
