using System.Collections.Immutable;
using System.Text;
using LinqContraband.Analyzers.LC011_EntityMissingPrimaryKey;
using LinqContraband.Analyzers.LC007_NPlusOneLooper;
using LinqContraband.Analyzers.LC015_MissingOrderBy;
using LinqContraband.Analyzers.LC017_WholeEntityProjection;
using LinqContraband.Analyzers.LC023_FindInsteadOfFirstOrDefault;
using LinqContraband.Analyzers.LC027_MissingExplicitForeignKey;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace LinqContraband.Tests.Architecture;

public class AnalyzerPerformanceTests
{
    private static readonly TimeSpan AnalyzerTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task LC023_PrimaryKeyLookup_CompletesOnLargeCompilation()
    {
        var compilation = CreateCompilation(GenerateEfCoreMock(), GenerateLc023StressSource());

        var diagnostics = await GetDiagnosticsWithinAsync(
            new FindInsteadOfFirstOrDefaultAnalyzer(),
            compilation,
            AnalyzerTimeout);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == FindInsteadOfFirstOrDefaultAnalyzer.DiagnosticId);
    }

    [Fact]
    public async Task LC023_PrimaryKeyLookup_CompletesOnManySyntaxTrees()
    {
        var compilation = CreateCompilation(GenerateLc023MultiTreeStressSources());

        await GetDiagnosticsWithinAsync(
            new FindInsteadOfFirstOrDefaultAnalyzer(),
            compilation,
            AnalyzerTimeout);
    }

    [Fact]
    public async Task SchemaAnalyzers_ConfigurationScans_CompleteOnLargeCompilation()
    {
        var compilation = CreateCompilation(GenerateEfCoreMock(), GenerateSchemaStressSource());

        await GetDiagnosticsWithinAsync(new EntityMissingPrimaryKeyAnalyzer(), compilation, AnalyzerTimeout);
        await GetDiagnosticsWithinAsync(new MissingExplicitForeignKeyAnalyzer(), compilation, AnalyzerTimeout);
    }

    [Fact]
    public async Task LC015_LocalQueryResolution_CompletesOnLargeMethod()
    {
        var compilation = CreateCompilation(GenerateEfCoreMock(), GenerateLc015StressSource());

        var diagnostics = await GetDiagnosticsWithinAsync(
            new MissingOrderByAnalyzer(),
            compilation,
            AnalyzerTimeout);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == MissingOrderByAnalyzer.DiagnosticId);
    }

    [Fact]
    public async Task LC015_LocalQueryResolution_CompletesOnSelfReferentialLocal()
    {
        var compilation = CreateCompilation(GenerateEfCoreMock(), GenerateLc015SelfReferentialSource());

        await GetDiagnosticsWithinAsync(
            new MissingOrderByAnalyzer(),
            compilation,
            AnalyzerTimeout);
    }

    [Fact]
    public async Task LC007_LocalQueryProvenance_CompletesOnLargeLoopMethod()
    {
        var compilation = CreateCompilation(GenerateEfCoreMock(), GenerateLc007StressSource());

        var diagnostics = await GetDiagnosticsWithinAsync(
            new NPlusOneLooperAnalyzer(),
            compilation,
            AnalyzerTimeout);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == NPlusOneLooperAnalyzer.DiagnosticId);
    }

    [Fact]
    public async Task LC017_WholeEntityUsage_CompletesOnManyMaterializers()
    {
        var compilation = CreateCompilation(GenerateEfCoreMock(), GenerateLc017StressSource());

        var diagnostics = await GetDiagnosticsWithinAsync(
            new WholeEntityProjectionAnalyzer(),
            compilation,
            AnalyzerTimeout);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == WholeEntityProjectionAnalyzer.DiagnosticId);
    }

    private static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsWithinAsync(
        DiagnosticAnalyzer analyzer,
        Compilation compilation,
        TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        var diagnosticsTask = compilation
            .WithAnalyzers(ImmutableArray.Create(analyzer))
            .GetAnalyzerDiagnosticsAsync(cancellation.Token);
        var completedTask = await Task.WhenAny(diagnosticsTask, Task.Delay(timeout));

        if (!ReferenceEquals(completedTask, diagnosticsTask))
            cancellation.Cancel();

        Assert.True(
            ReferenceEquals(completedTask, diagnosticsTask),
            $"{analyzer.GetType().Name} did not complete within {timeout}.");

        return await diagnosticsTask;
    }

    private static Compilation CreateCompilation(params string[] sources)
    {
        var syntaxTrees = sources.Select((source, index) =>
            CSharpSyntaxTree.ParseText(
                source,
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest),
                path: $"Perf{index}.cs"));

        var trustedPlatformAssemblies =
            ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))!
            .Split(Path.PathSeparator)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path));

        return CSharpCompilation.Create(
            "AnalyzerPerformance",
            syntaxTrees,
            trustedPlatformAssemblies,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static string GenerateLc015StressSource()
    {
        const int queryCount = 240;
        const int noiseCount = 500;
        var source = new StringBuilder();
        source.AppendLine(
            """
            using System.Linq;
            using Microsoft.EntityFrameworkCore;

            namespace PerfApp;

            public class AppDbContext : DbContext
            {
                public DbSet<User> Users { get; set; }
            }

            public class User
            {
                public int Id { get; set; }
                public string Name { get; set; }
            }

            public class Queries
            {
                public void Run()
                {
                    var db = new AppDbContext();
                    var query = db.Users.Where(u => u.Id > 0);
            """);

        for (var i = 0; i < noiseCount; i++)
            source.AppendLine($"        var noise{i} = {i};");

        for (var i = 0; i < queryCount; i++)
            source.AppendLine($"        var page{i} = query.Skip({i});");

        source.AppendLine(
            """
                }
            }
            """);

        return source.ToString();
    }

    private static string GenerateLc015SelfReferentialSource()
    {
        return
            """
            using System.Linq;
            using Microsoft.EntityFrameworkCore;

            namespace PerfApp;

            public class User
            {
                public int Id { get; set; }
            }

            public class Queries
            {
                public void Run()
                {
                    IQueryable<User> query = query;
                    var page = query.Skip(1);
                }
            }
            """;
    }

    private static string GenerateLc007StressSource()
    {
        const int queryCount = 160;
        const int noiseCount = 400;
        var source = new StringBuilder();
        source.AppendLine(
            """
            using System.Collections.Generic;
            using System.Linq;
            using Microsoft.EntityFrameworkCore;

            namespace PerfApp;

            public class AppDbContext : DbContext
            {
                public DbSet<User> Users { get; set; }
            }

            public class User
            {
                public int Id { get; set; }
                public string Name { get; set; }
            }

            public class Queries
            {
                public void Run(List<int> ids)
                {
                    var db = new AppDbContext();
                    var query = db.Users.Where(u => u.Id > 0);
            """);

        for (var i = 0; i < noiseCount; i++)
            source.AppendLine($"        var noise{i} = {i};");

        source.AppendLine(
            """
                    foreach (var id in ids)
                    {
            """);

        for (var i = 0; i < queryCount; i++)
            source.AppendLine($"            var users{i} = query.Where(u => u.Id == id).ToList();");

        source.AppendLine(
            """
                    }
                }
            }
            """);

        return source.ToString();
    }

    private static string GenerateLc017StressSource()
    {
        const int materializerCount = 80;
        var source = new StringBuilder();
        source.AppendLine(
            """
            using System;
            using System.Linq;
            using Microsoft.EntityFrameworkCore;

            namespace PerfApp;

            public class AppDbContext : DbContext
            {
                public DbSet<LargeEntity> LargeEntities { get; set; }
            }

            public class LargeEntity
            {
                public int Id { get; set; }
                public string Name { get; set; }
                public string P01 { get; set; }
                public string P02 { get; set; }
                public string P03 { get; set; }
                public string P04 { get; set; }
                public string P05 { get; set; }
                public string P06 { get; set; }
                public string P07 { get; set; }
                public string P08 { get; set; }
                public string P09 { get; set; }
                public string P10 { get; set; }
            }

            public class Queries
            {
                public void Run()
                {
                    var db = new AppDbContext();
            """);

        for (var i = 0; i < materializerCount; i++)
        {
            source.AppendLine($"        var entities{i} = db.LargeEntities.ToList();");
            source.AppendLine($"        foreach (var entity{i} in entities{i})");
            source.AppendLine("        {");
            source.AppendLine($"            Console.WriteLine(entity{i}.Name);");
            source.AppendLine("        }");
        }

        source.AppendLine(
            """
                }
            }
            """);

        return source.ToString();
    }

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

    private static string GenerateSchemaStressSource()
    {
        const int entityCount = 60;
        const int contextCount = 8;
        var source = new StringBuilder();
        source.AppendLine(
            """
            using Microsoft.EntityFrameworkCore;

            namespace PerfApp;
            """);

        for (var i = 0; i < contextCount; i++)
        {
            source.AppendLine($"public class AppDbContext{i} : DbContext");
            source.AppendLine("{");
            for (var j = 0; j < entityCount; j++)
                source.AppendLine($"    public DbSet<Entity{j}> Entities{j} {{ get; set; }}");

            source.AppendLine("    protected override void OnModelCreating(ModelBuilder modelBuilder)");
            source.AppendLine("    {");
            source.AppendLine($"        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext{i}).Assembly);");
            source.AppendLine("    }");
            source.AppendLine("}");
        }

        for (var i = 0; i < entityCount; i++)
        {
            var related = (i + 1) % entityCount;
            source.AppendLine($$"""
                public class Entity{{i}}
                {
                    public int CustomId { get; set; }
                    public int Entity{{related}}Id { get; set; }
                    public Entity{{related}} Entity{{related}} { get; set; }
                }

                public class Entity{{i}}Configuration : IEntityTypeConfiguration<Entity{{i}}>
                {
                    public void Configure(EntityTypeBuilder<Entity{{i}}> builder)
                    {
                        builder.HasKey(e => e.CustomId);
                        builder.HasOne(e => e.Entity{{related}}).WithOne().HasForeignKey<Entity{{i}}>(e => e.Entity{{related}}Id);
                    }
                }
                """);
        }

        return source.ToString();
    }

    private static string GenerateEfCoreMock()
    {
        return
            """
            using System;
            using System.Collections;
            using System.Collections.Generic;
            using System.Linq;
            using System.Linq.Expressions;
            using System.Reflection;
            using System.Threading;
            using System.Threading.Tasks;

            namespace Microsoft.EntityFrameworkCore
            {
                public class DbContext
                {
                    protected virtual void OnModelCreating(ModelBuilder modelBuilder) { }
                }

                public class DbSet<TEntity> : IQueryable<TEntity> where TEntity : class
                {
                    public TEntity Find(params object[] keyValues) => null;
                    public ValueTask<TEntity> FindAsync(params object[] keyValues) => default;
                    public ValueTask<TEntity> FindAsync(object[] keyValues, CancellationToken cancellationToken) => default;
                    public Type ElementType => typeof(TEntity);
                    public Expression Expression => null;
                    public IQueryProvider Provider => null;
                    public IEnumerator<TEntity> GetEnumerator() => null;
                    IEnumerator IEnumerable.GetEnumerator() => null;
                }

                public interface IEntityTypeConfiguration<TEntity> where TEntity : class
                {
                    void Configure(EntityTypeBuilder<TEntity> builder);
                }

                public class ModelBuilder
                {
                    public EntityTypeBuilder<TEntity> Entity<TEntity>() where TEntity : class => new EntityTypeBuilder<TEntity>();
                    public void ApplyConfiguration<TEntity>(IEntityTypeConfiguration<TEntity> configuration) where TEntity : class { }
                    public void ApplyConfigurationsFromAssembly(Assembly assembly) { }
                }

                public class EntityTypeBuilder<TEntity> where TEntity : class
                {
                    public EntityTypeBuilder<TEntity> HasKey<TProperty>(Expression<Func<TEntity, TProperty>> keyExpression) => this;
                    public EntityTypeBuilder<TEntity> HasNoKey() => this;
                    public OwnedNavigationBuilder<TEntity, TDependent> OwnsOne<TDependent>(Expression<Func<TEntity, TDependent>> navigationExpression) where TDependent : class => new OwnedNavigationBuilder<TEntity, TDependent>();
                    public ReferenceNavigationBuilder<TEntity, TRelated> HasOne<TRelated>(Expression<Func<TEntity, TRelated>> navigationExpression = null) where TRelated : class => new ReferenceNavigationBuilder<TEntity, TRelated>();
                }

                public class OwnedNavigationBuilder<TEntity, TDependent>
                    where TEntity : class
                    where TDependent : class
                {
                }

                public class ReferenceNavigationBuilder<TEntity, TRelated>
                    where TEntity : class
                    where TRelated : class
                {
                    public ReferenceReferenceBuilder<TEntity, TRelated> WithOne(Expression<Func<TRelated, TEntity>> navigationExpression = null) => new ReferenceReferenceBuilder<TEntity, TRelated>();
                }

                public class ReferenceReferenceBuilder<TEntity, TRelated>
                    where TEntity : class
                    where TRelated : class
                {
                    public ReferenceReferenceBuilder<TEntity, TRelated> HasForeignKey<TDependentEntity>(Expression<Func<TDependentEntity, object>> foreignKeyExpression) where TDependentEntity : class => this;
                }

                public static class EntityFrameworkQueryableExtensions
                {
                    public static Task<TSource> FirstOrDefaultAsync<TSource>(this IQueryable<TSource> source, Expression<Func<TSource, bool>> predicate, CancellationToken cancellationToken = default) => Task.FromResult(default(TSource));
                    public static Task<TSource> SingleOrDefaultAsync<TSource>(this IQueryable<TSource> source, Expression<Func<TSource, bool>> predicate, CancellationToken cancellationToken = default) => Task.FromResult(default(TSource));
                }
            }
            """;
    }
}
