using System.Diagnostics;

namespace LinqContraband.Tests.Analyzers.LC046_ConcurrentDbContextOperations;

public sealed class ConcurrentDbContextOperationsModernCompilerTests
{
    [Fact]
    public async Task SingletonTaskArrayCollectionExpressionAwaitedWhenAny_ShouldNotTrigger()
    {
        await VerifyCurrentCompilerAsync(
            """
                        var first = db.Users.ToListAsync();
                        Task[] tasks = [first];
                        await Task.WhenAny(tasks);
                        await db.Users.AnyAsync();
            """,
            expectsLc046: false);
    }

    [Fact]
    public async Task DirectSingletonCollectionExpressionAwaitedWhenAny_ShouldNotTrigger()
    {
        await VerifyCurrentCompilerAsync(
            """
                        var first = db.Users.ToListAsync();
                        await Task.WhenAny([first]);
                        await db.Users.AnyAsync();
            """,
            expectsLc046: false);
    }

    [Fact]
    public async Task DirectSingletonCollectionExpressionWhenAnyOomCatch_ShouldTrigger()
    {
        await VerifyCurrentCompilerAsync(
            """
                        var first = db.Users.ToListAsync();
                        try
                        {
                            await Task.WhenAny([first]);
                        }
                        catch (OutOfMemoryException)
                        {
                        }

                        await db.Users.AnyAsync();
            """,
            expectsLc046: true);
    }

    [Fact]
    public async Task ParenthesizedSingletonCollectionExpressionAwaitedWhenAny_ShouldNotTrigger()
    {
        await VerifyCurrentCompilerAsync(
            """
                        var first = db.Users.ToListAsync();
                        await Task.WhenAny([(first)]);
                        await db.Users.AnyAsync();
            """,
            expectsLc046: false);
    }

    [Fact]
    public async Task NullForgivenSingletonCollectionExpressionAwaitedWhenAny_ShouldNotTrigger()
    {
        await VerifyCurrentCompilerAsync(
            """
                        var first = db.Users.ToListAsync();
                        await Task.WhenAny([first!]);
                        await db.Users.AnyAsync();
            """,
            expectsLc046: false);
    }

    [Fact]
    public async Task BaseCastSingletonCollectionExpressionAwaitedWhenAny_ShouldNotTrigger()
    {
        await VerifyCurrentCompilerAsync(
            """
                        var first = db.Users.ToListAsync();
                        await Task.WhenAny([(Task)first]);
                        await db.Users.AnyAsync();
            """,
            expectsLc046: false);
    }

    [Fact]
    public async Task DirectTaskElementInSingletonCollectionExpressionAwaitedWhenAny_ShouldNotTrigger()
    {
        await VerifyCurrentCompilerAsync(
            """
                        Task[] tasks = [db.Users.ToListAsync()];
                        await Task.WhenAny(tasks);
                        await db.Users.AnyAsync();
            """,
            expectsLc046: false);
    }

    [Fact]
    public async Task DirectWrappedTaskInSingletonCollectionExpressionAwaitedWhenAny_ShouldTrigger()
    {
        await VerifyCurrentCompilerAsync(
            """
                        var first = db.Users.ToListAsync();
                        await Task.WhenAny([Task.FromResult(first)]);
                        await db.Users.AnyAsync();
            """,
            expectsLc046: true);
    }

    [Fact]
    public async Task CollectionExpressionAllocationCanBypassSingletonWhenAny_ShouldTrigger()
    {
        await VerifyCurrentCompilerAsync(
            """
                        var first = db.Users.ToListAsync();
                        try
                        {
                            Task[] tasks = [first];
                            await Task.WhenAny(tasks);
                        }
                        catch (OutOfMemoryException)
                        {
                        }

                        await db.Users.AnyAsync();
            """,
            expectsLc046: true);
    }

    [Fact]
    public async Task CollectionExpressionConvertedToEnumerableCanBypassSingletonWhenAny_ShouldTrigger()
    {
        await VerifyCurrentCompilerAsync(
            """
                        var first = db.Users.ToListAsync();
                        try
                        {
                            await Task.WhenAny((IEnumerable<Task>)[first]);
                        }
                        catch (OutOfMemoryException)
                        {
                        }

                        await db.Users.AnyAsync();
            """,
            expectsLc046: true);
    }

    [Fact]
    public async Task CollectionExpressionNullElementWithNarrowCatch_ShouldNotTrigger()
    {
        await VerifyCurrentCompilerAsync(
            """
                        var first = db.Users.ToListAsync();
                        try
                        {
                            await Task.WhenAll([first, null!]);
                        }
                        catch (ArgumentNullException)
                        {
                        }

                        await db.Users.AnyAsync();
            """,
            expectsLc046: false);
    }

    [Fact]
    public async Task CollectionExpressionAwaitedWhenAllInContinuingTry_ShouldNotTrigger()
    {
        await VerifyCurrentCompilerAsync(
            """
                        var first = db.Users.ToListAsync();
                        Task[] tasks = [first];
                        try
                        {
                            await Task.WhenAll(tasks);
                        }
                        catch
                        {
                        }

                        await db.Users.AnyAsync();
            """,
            expectsLc046: false);
    }

    [Fact]
    public async Task WrappedEfTaskInCollectionExpressionAwaitedWhenAny_ShouldTrigger()
    {
        await VerifyCurrentCompilerAsync(
            """
                        var first = db.Users.ToListAsync();
                        Task[] tasks = [Task.FromResult(first)];
                        await Task.WhenAny(tasks);
                        await db.Users.AnyAsync();
            """,
            expectsLc046: true);
    }

    [Fact]
    public void DiagnosticMatcher_ShouldRejectAnalyzerCrashText()
    {
        const string diagnostic =
            "/tmp/Probe.cs(1,1): warning LC046: concurrent operations";
        const string analyzerCrash =
            "warning AD0001: Analyzer 'LC046_ConcurrentDbContextOperations' threw an exception";

        Assert.True(ContainsDiagnostic(diagnostic, "LC046"));
        Assert.False(ContainsDiagnostic(analyzerCrash, "LC046"));
    }

    private static async Task VerifyCurrentCompilerAsync(
        string methodBody,
        bool expectsLc046)
    {
        var analyzerPath = typeof(
            LinqContraband.Analyzers.LC046_ConcurrentDbContextOperations
                .ConcurrentDbContextOperationsAnalyzer).Assembly.Location;
        var probeDirectory = Path.Combine(
            Path.GetTempPath(),
            $"linqcontraband-lc046-{Guid.NewGuid():N}");
        Directory.CreateDirectory(probeDirectory);

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(probeDirectory, "Probe.csproj"),
                $"""
                 <Project Sdk="Microsoft.NET.Sdk">
                   <PropertyGroup>
                     <TargetFramework>net10.0</TargetFramework>
                     <LangVersion>latest</LangVersion>
                     <Nullable>enable</Nullable>
                   </PropertyGroup>
                   <ItemGroup>
                     <Analyzer Include="{analyzerPath}" />
                   </ItemGroup>
                 </Project>
                 """);
            const string sourcePrefix =
                """
                using System;
                using System.Collections;
                using System.Collections.Generic;
                using System.Linq;
                using System.Linq.Expressions;
                using System.Threading.Tasks;
                using Microsoft.EntityFrameworkCore;

                namespace Microsoft.EntityFrameworkCore
                {
                    public class DbContext { }

                    public class DbSet<TEntity> : IQueryable<TEntity> where TEntity : class
                    {
                        public Type ElementType => typeof(TEntity);
                        public Expression Expression => Expression.Constant(this);
                        public IQueryProvider Provider => null!;
                        public IEnumerator<TEntity> GetEnumerator() => null!;
                        IEnumerator IEnumerable.GetEnumerator() => null!;
                    }

                    public static class EntityFrameworkQueryableExtensions
                    {
                        public static Task<List<TEntity>> ToListAsync<TEntity>(
                            this IQueryable<TEntity> source) =>
                            Task.FromResult(new List<TEntity>());

                        public static Task<bool> AnyAsync<TEntity>(
                            this IQueryable<TEntity> source) =>
                            Task.FromResult(false);
                    }
                }

                public sealed class User { }

                public sealed class AppDbContext : DbContext
                {
                    public DbSet<User> Users { get; } = new();
                }

                public sealed class Program
                {
                    public async Task Run(AppDbContext db)
                    {
                """;
            const string sourceSuffix =
                """
                    }
                }
                """;
            await File.WriteAllTextAsync(
                Path.Combine(probeDirectory, "Probe.cs"),
                sourcePrefix + methodBody + sourceSuffix);

            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "build Probe.csproj --nologo --verbosity minimal",
                WorkingDirectory = probeDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            })!;
            var standardOutput = await process.StandardOutput.ReadToEndAsync();
            var standardError = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var output = standardOutput + standardError;

            Assert.Equal(0, process.ExitCode);
            Assert.DoesNotContain("AD0001", output, StringComparison.Ordinal);
            var containsLc046 = ContainsDiagnostic(output, "LC046");
            if (expectsLc046)
                Assert.True(containsLc046, output);
            else
                Assert.False(containsLc046, output);
        }
        finally
        {
            Directory.Delete(probeDirectory, recursive: true);
        }
    }

    private static bool ContainsDiagnostic(string output, string diagnosticId)
    {
        return output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Any(line =>
                line.Contains($": warning {diagnosticId}:", StringComparison.Ordinal) ||
                line.Contains($": error {diagnosticId}:", StringComparison.Ordinal));
    }
}
