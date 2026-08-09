using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;

namespace LinqContraband.Analyzers.LC045_MissingInclude;

public sealed partial class MissingIncludeAnalyzer
{
    /// <summary>
    /// The well-known symbols LC045 compares against, resolved once per compilation.
    ///
    /// They were previously resolved with <c>GetTypeByMetadataName</c> at each comparison, which
    /// runs per operation: every invocation in a file — <c>Console.WriteLine</c> included — paid
    /// several metadata name lookups before being rejected. On a file of 2,000 ordinary LINQ
    /// calls containing no EF code at all, that dominated the rule's entire cost.
    ///
    /// Keyed weakly by <see cref="Compilation"/> so the entry dies with it, which matters in an
    /// IDE where a new compilation is produced on nearly every keystroke.
    /// </summary>
    private sealed class WellKnownSymbols
    {
        private static readonly ConditionalWeakTable<Compilation, WellKnownSymbols> Cache = new();

        private WellKnownSymbols(Compilation compilation)
        {
            Enumerable = compilation.GetTypeByMetadataName("System.Linq.Enumerable");
            Queryable = compilation.GetTypeByMetadataName("System.Linq.Queryable");
            OrderedEnumerable = compilation.GetTypeByMetadataName("System.Linq.IOrderedEnumerable`1");
            EntityFrameworkQueryableExtensions = compilation.GetTypeByMetadataName(
                "Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions"
            );
            RelationalQueryableExtensions = compilation.GetTypeByMetadataName(
                "Microsoft.EntityFrameworkCore.RelationalQueryableExtensions"
            );
            DbSet = compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.DbSet`1");
            List = compilation.GetTypeByMetadataName("System.Collections.Generic.List`1");
            EqualityComparer = compilation.GetTypeByMetadataName(
                "System.Collections.Generic.IEqualityComparer`1"
            );
            CancellationToken = compilation.GetTypeByMetadataName(
                "System.Threading.CancellationToken"
            );
            Expression = compilation.GetTypeByMetadataName("System.Linq.Expressions.Expression`1");
            Func = compilation.GetTypeByMetadataName("System.Func`2");
            FormattableString = compilation.GetTypeByMetadataName("System.FormattableString");
        }

        public INamedTypeSymbol? Enumerable { get; }
        public INamedTypeSymbol? Queryable { get; }
        public INamedTypeSymbol? OrderedEnumerable { get; }
        public INamedTypeSymbol? EntityFrameworkQueryableExtensions { get; }
        public INamedTypeSymbol? RelationalQueryableExtensions { get; }
        public INamedTypeSymbol? DbSet { get; }
        public INamedTypeSymbol? List { get; }
        public INamedTypeSymbol? EqualityComparer { get; }
        public INamedTypeSymbol? CancellationToken { get; }
        public INamedTypeSymbol? Expression { get; }
        public INamedTypeSymbol? Func { get; }
        public INamedTypeSymbol? FormattableString { get; }

        public static WellKnownSymbols For(Compilation compilation)
        {
            return Cache.GetValue(compilation, static key => new WellKnownSymbols(key));
        }
    }
}
