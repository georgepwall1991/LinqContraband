using System.Collections.Immutable;
using LinqContraband.Analyzers.LC004_IQueryableLeak;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace LinqContraband.Tests.Analyzers.LC004_IQueryableLeak;

public class IQueryableLeakCrossCompilationTests
{
    [Fact]
    public async Task Leak_WhenCalleeBodyLivesInReferencedCompilation_ShouldNotCrashAndStayQuiet()
    {
        var library = CreateCompilation(
            "Library",
            """
            using System;
            using System.Collections;
            using System.Collections.Generic;
            using System.Linq;
            using System.Linq.Expressions;

            namespace TestNamespace
            {
                public class User
                {
                    public int Id { get; set; }
                }

                public class DbContext : IDisposable
                {
                    public void Dispose() { }
                }

                public class DbSet<T> : IQueryable<T>
                {
                    public Type ElementType => typeof(T);
                    public Expression Expression => Expression.Constant(this);
                    public IQueryProvider Provider => null;
                    public IEnumerator<T> GetEnumerator() => null;
                    IEnumerator IEnumerable.GetEnumerator() => null;
                }

                public sealed class AppDbContext : DbContext
                {
                    public DbSet<User> Users { get; set; }
                }

                public static class Helpers
                {
                    public static void ProcessUsers(IEnumerable<User> users)
                    {
                        foreach (var user in users)
                        {
                            Console.WriteLine(user.Id);
                        }
                    }
                }
            }
            """);

        var app = CreateCompilation(
            "App",
            """
            using System.Linq;
            using TestNamespace;

            namespace TestApp
            {
                public sealed class Program
                {
                    public void Main()
                    {
                        using var db = new AppDbContext();
                        var query = db.Users.Where(u => u.Id > 10);
                        Helpers.ProcessUsers(query);
                    }
                }
            }
            """,
            library.ToMetadataReference());

        var diagnostics = await app
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new IQueryableLeakAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AD0000");
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == IQueryableLeakAnalyzer.DiagnosticId);
    }

    private static Compilation CreateCompilation(string assemblyName, string source, params MetadataReference[] extraReferences)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, path: $"{assemblyName}.cs");
        var references = GetMetadataReferences().Concat(extraReferences);

        return CSharpCompilation.Create(
            assemblyName,
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static IEnumerable<MetadataReference> GetMetadataReferences()
    {
        var trustedPlatformAssemblies =
            ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))!
            .Split(Path.PathSeparator);

        foreach (var path in trustedPlatformAssemblies)
        {
            var assemblyName = Path.GetFileNameWithoutExtension(path);
            if (assemblyName is "System.Runtime" or "System.Linq" or "System.Linq.Expressions" or
                "System.Collections" or "netstandard" or "System.ObjectModel" or "System.Memory" or
                "System.Private.CoreLib" or "System.Console")
            {
                yield return MetadataReference.CreateFromFile(path);
            }
        }
    }
}
