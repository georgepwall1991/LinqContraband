using Microsoft.CodeAnalysis.Testing;

namespace LinqContraband.Tests.Analyzers.LC018_AvoidFromSqlRawWithInterpolation;

/// <summary>
/// A stand-in for the real Microsoft.EntityFrameworkCore.Relational assembly, compiled as its own
/// project so the raw-SQL extension methods carry a real assembly identity and version. LC018 and
/// LC034 use that identity to tell when EF Core's own EF1002/EF1003 analyzers already report a call.
/// </summary>
internal static class EfRelationalAssemblyMock
{
    public const string AssemblyName = "Microsoft.EntityFrameworkCore.Relational";

    public static void AddTo(SolutionState state, string version)
    {
        var project = state.AdditionalProjects[AssemblyName];
        project.Sources.Add(("Relational.cs", Source(version)));
        state.AdditionalProjectReferences.Add(AssemblyName);
    }

    private static string Source(string version) => $@"
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

[assembly: System.Reflection.AssemblyVersion(""{version}"")]

namespace Microsoft.EntityFrameworkCore.Infrastructure
{{
    public class DatabaseFacade {{ }}
}}

namespace Microsoft.EntityFrameworkCore
{{
    using Microsoft.EntityFrameworkCore.Infrastructure;

    public class DbContext
    {{
        public DatabaseFacade Database {{ get; }} = new DatabaseFacade();
        public DbSet<TEntity> Set<TEntity>() where TEntity : class => null;
    }}

    public abstract class DbSet<TEntity> : IQueryable<TEntity> where TEntity : class
    {{
        public Type ElementType => typeof(TEntity);
        public Expression Expression => null;
        public IQueryProvider Provider => null;
        public IEnumerator<TEntity> GetEnumerator() => null;
        IEnumerator IEnumerable.GetEnumerator() => null;
    }}

    public static class RelationalQueryableExtensions
    {{
        public static IQueryable<TEntity> FromSqlRaw<TEntity>(this DbSet<TEntity> source, string sql, params object[] parameters) where TEntity : class => source;
        public static IQueryable<TEntity> FromSql<TEntity>(this DbSet<TEntity> source, FormattableString sql) where TEntity : class => source;
        public static IQueryable<TEntity> FromSqlInterpolated<TEntity>(this DbSet<TEntity> source, FormattableString sql) where TEntity : class => source;
    }}

    public static class RelationalDatabaseFacadeExtensions
    {{
        public static int ExecuteSqlRaw(this DatabaseFacade databaseFacade, string sql, params object[] parameters) => 0;
        public static Task<int> ExecuteSqlRawAsync(this DatabaseFacade databaseFacade, string sql, params object[] parameters) => Task.FromResult(0);
        public static int ExecuteSql(this DatabaseFacade databaseFacade, FormattableString sql) => 0;
        public static IQueryable<TResult> SqlQueryRaw<TResult>(this DatabaseFacade databaseFacade, string sql, params object[] parameters) => null;
        public static IQueryable<TResult> SqlQuery<TResult>(this DatabaseFacade databaseFacade, FormattableString sql) => null;
    }}
}}
";
}
