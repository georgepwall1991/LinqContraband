using System.Text;

namespace LinqContraband.Tests.Architecture;

public partial class AnalyzerPerformanceTests
{
    private static string GenerateLc023StressSource()
    {
        const int entityCount = 40;
        const int queryCount = 160;
        const int noiseCount = 300;
        var source = new StringBuilder();
        source.AppendLine(
            """
            using System.Linq;
            using Microsoft.EntityFrameworkCore;

            namespace PerfApp;

            public class AppDbContext : DbContext
            {
                protected override void OnModelCreating(ModelBuilder modelBuilder)
                {
            """);

        for (var i = 0; i < entityCount; i++)
            source.AppendLine($"        modelBuilder.Entity<Entity{i}>().HasKey(e => e.ExternalId);");

        source.AppendLine(
            """
                }
            }
            """);

        for (var i = 0; i < entityCount; i++)
            source.AppendLine($"public class Entity{i} {{ public int Id {{ get; set; }} public int ExternalId {{ get; set; }} }}");

        source.AppendLine(
            """
            public static class Noise
            {
                public static void HasKey(int value) { }
                public static void Run()
                {
            """);

        for (var i = 0; i < noiseCount; i++)
            source.AppendLine($"        HasKey({i});");

        source.AppendLine(
            """
                }
            }

            public class Queries
            {
                public void Run(int id)
                {
            """);

        for (var i = 0; i < queryCount; i++)
        {
            var entityIndex = i % entityCount;
            source.AppendLine($"        var set{i} = new DbSet<Entity{entityIndex}>();");
            source.AppendLine($"        var query{i} = set{i}.FirstOrDefault(e => e.ExternalId == id);");
        }

        source.AppendLine(
            """
                }
            }
            """);

        return source.ToString();
    }

    private static string[] GenerateLc023MultiTreeStressSources()
    {
        const int entityCount = 50;
        const int queryFileCount = 160;
        var sources = new List<string> { GenerateEfCoreMock(), GenerateLc023ModelSource(entityCount) };

        for (var i = 0; i < queryFileCount; i++)
        {
            var entityIndex = i % entityCount;
            sources.Add($$"""
                using System.Linq;
                using Microsoft.EntityFrameworkCore;

                namespace PerfApp;

                public static class Noise{{i}}
                {
                    public static void HasKey(int value) { }
                    public static void Run()
                    {
                        HasKey({{i}});
                    }
                }

                public class Query{{i}}
                {
                    public void Run(int id)
                    {
                        var set = new DbSet<Entity{{entityIndex}}>();
                        var query = set.FirstOrDefault(e => e.ExternalId == id);
                    }
                }
                """);
        }

        return sources.ToArray();
    }

    private static string GenerateLc023ModelSource(int entityCount)
    {
        var source = new StringBuilder();
        source.AppendLine(
            """
            using Microsoft.EntityFrameworkCore;

            namespace PerfApp;

            public class AppDbContext : DbContext
            {
                protected override void OnModelCreating(ModelBuilder modelBuilder)
                {
            """);

        for (var i = 0; i < entityCount; i++)
            source.AppendLine($"        modelBuilder.Entity<Entity{i}>().HasKey(e => e.ExternalId);");

        source.AppendLine(
            """
                }
            }
            """);

        for (var i = 0; i < entityCount; i++)
            source.AppendLine($"public class Entity{i} {{ public int Id {{ get; set; }} public int ExternalId {{ get; set; }} }}");

        return source.ToString();
    }
}
