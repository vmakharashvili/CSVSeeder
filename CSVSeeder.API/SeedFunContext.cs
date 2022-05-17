using Microsoft.EntityFrameworkCore;
using CSVSeeder.API.Models;

namespace CSVSeeder.API;

public class SeedFunContext : DbContext
{
    public SeedFunContext()
    {
       
    }

    public SeedFunContext(DbContextOptions<SeedFunContext> options): base(options)
    {
       
    }

    public DbSet<User> User => Set<User>();
    public DbSet<TodoItem> TodoItems => Set<TodoItem>();
    public DbSet<EntityData> EntityData => Set<EntityData>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TodoItem>().ToTable("TodoItems", "tt");
        modelBuilder.Entity<TodoItem>().Property(t => t.Id).ValueGeneratedOnAdd();

        modelBuilder.Entity<User>().ToTable("Users", "tt");
        modelBuilder.Entity<User>().HasMany(x => x.TodoItems).WithOne(x => x.User).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<EntityData>().ToTable("EntityData", "tt");
    }
}
