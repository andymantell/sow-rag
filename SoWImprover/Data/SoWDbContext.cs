using Microsoft.EntityFrameworkCore;
using SoWImprover.Models;

namespace SoWImprover.Data;

public class SoWDbContext(DbContextOptions<SoWDbContext> options) : DbContext(options)
{
    public DbSet<DocumentEntity> Documents => Set<DocumentEntity>();
    public DbSet<SectionEntity> Sections => Set<SectionEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DocumentEntity>(e =>
        {
            e.HasKey(d => d.Id);
            e.HasMany(d => d.Sections)
                .WithOne(s => s.Document)
                .HasForeignKey(s => s.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SectionEntity>(e =>
        {
            e.HasKey(s => s.Id);
            e.HasIndex(s => new { s.DocumentId, s.SectionIndex });
        });
    }
}
