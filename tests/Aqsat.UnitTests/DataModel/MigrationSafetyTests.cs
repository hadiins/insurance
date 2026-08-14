namespace Aqsat.UnitTests.DataModel;

/// <summary>
/// CLAUDE.md rule 6: EF Core defaults to Cascade — every generated migration must be read line by
/// line to confirm the global Restrict loop in AppDbContext actually ran. This makes that check
/// part of the build instead of relying on someone remembering to look.
/// </summary>
public class MigrationSafetyTests
{
    [Fact]
    public void No_migration_contains_a_cascade_delete_behaviour()
    {
        var migrationsDir = FindMigrationsDirectory();
        var migrationFiles = Directory.GetFiles(migrationsDir, "*.cs", SearchOption.TopDirectoryOnly)
            .Where(f => !f.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.NotEmpty(migrationFiles);

        var offenders = migrationFiles
            .Select(f => (File: f, Content: File.ReadAllText(f)))
            .Where(x => x.Content.Contains("ReferentialAction.Cascade", StringComparison.Ordinal))
            .Select(x => Path.GetFileName(x.File))
            .ToList();

        Assert.True(offenders.Count == 0, $"Cascade delete found in: {string.Join(", ", offenders)}");
    }

    private static string FindMigrationsDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Aqsat.Infrastructure", "Migrations");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate src/Aqsat.Infrastructure/Migrations by walking up from the test output directory.");
    }
}
