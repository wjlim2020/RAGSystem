using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using System.Linq;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    public DbSet<Document> Documents { get; set; }
    public DbSet<ChatHistory> ChatHistories { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var floatArrayConverter = new ValueConverter<float[], string>(
            v => v != null ? string.Join(",", v.Select(f => f.ToString(CultureInfo.InvariantCulture))) : "",  // ✅ Convert float[] to CSV string
            v => string.IsNullOrEmpty(v)
                ? Array.Empty<float>()  // ✅ Handle empty/null cases
                : v.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(f => float.Parse(f, CultureInfo.InvariantCulture))  // ✅ Convert back safely
                    .ToArray()
        );

        modelBuilder.Entity<Document>()
            .Property(d => d.Embedding)
            .HasConversion(floatArrayConverter);  // ✅ Apply the conversion
    }
}



public class Document
{
    public int Id { get; set; }
    public string FileName { get; set; }
    public string Content { get; set; }
    public float[] Embedding { get; set; }  // ✅ Store float[] as string
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;  // Optional timestamp
}


