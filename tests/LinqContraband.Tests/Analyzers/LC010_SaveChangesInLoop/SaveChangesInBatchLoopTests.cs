namespace LinqContraband.Tests.Analyzers.LC010_SaveChangesInLoop;

/// <summary>
/// Large jobs save once per batch so the change tracker stays small: a <c>foreach</c> over <c>Chunk(...)</c>, or a
/// loop that pages, keysets or drains the database a batch at a time (the loops LC007 exempts). LC010 stays quiet on
/// those, and still reports a save per item, and batches repeated by an outer per-item loop.
/// </summary>
public class SaveChangesInBatchLoopTests
{
    private const string Mock = @"
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Microsoft.EntityFrameworkCore
{
    public class DbContext
    {
        public int SaveChanges() => 0;
        public Task<int> SaveChangesAsync() => Task.FromResult(0);
    }

    public class DbSet<T> : IQueryable<T> where T : class
    {
        public void AddRange(IEnumerable<T> entities) { }
        public Type ElementType => typeof(T);
        public Expression Expression => Expression.Constant(this);
        public IQueryProvider Provider => null;
        public IEnumerator<T> GetEnumerator() => null;
        IEnumerator IEnumerable.GetEnumerator() => null;
    }

    public static class EntityFrameworkQueryableExtensions
    {
        public static Task<List<T>> ToListAsync<T>(this IQueryable<T> source) => Task.FromResult(new List<T>());
        public static Task<int> CountAsync<T>(this IQueryable<T> source) => Task.FromResult(0);
    }
}

public class User { public int Id { get; set; } public string Name { get; set; } }

public class AppDbContext : DbContext
{
    public DbSet<User> Users { get; set; }
}
";

    private static string Program(string body) => Mock + @"
class Program
{
    async Task Run(AppDbContext db, int[] ids, User[] users, int[] tenants, int size)
    {
        " + body + @"
    }
}
";

    // Enumerable.Chunk needs .NET 6 or later reference assemblies.
    private static Task VerifyAsync(string body) =>
        new Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<
            LinqContraband.Analyzers.LC010_SaveChangesInLoop.SaveChangesInLoopAnalyzer,
            Microsoft.CodeAnalysis.Testing.Verifiers.XUnitVerifier>
        {
            TestCode = Program(body),
            ReferenceAssemblies = Microsoft.CodeAnalysis.Testing.ReferenceAssemblies.Net.Net80,
        }.RunAsync();

    [Theory]
    // Insert in chunks.
    [InlineData(@"foreach (var batch in users.Chunk(1000))
        {
            db.Users.AddRange(batch);
            await db.SaveChangesAsync();
        }")]
    // Load a chunk, change it, save it (Kavita's activity-data migration).
    [InlineData(@"foreach (var batch in ids.Chunk(1000))
        {
            var rows = await db.Users.Where(u => batch.Contains(u.Id)).ToListAsync();
            foreach (var row in rows) row.Name = row.Name.Trim();
            await db.SaveChangesAsync();
        }")]
    // Paged by a counted loop (Kavita's reading-session migration).
    [InlineData(@"var pages = (await db.Users.CountAsync() + size - 1) / size;
        for (var page = 0; page < pages; page++)
        {
            var rows = await db.Users.OrderBy(u => u.Id).Skip(page * size).Take(size).ToListAsync();
            foreach (var row in rows) row.Name = row.Name.Trim();
            await db.SaveChangesAsync();
        }")]
    // Paged by an offset the body advances.
    [InlineData(@"var total = await db.Users.CountAsync();
        var skip = 0;
        while (skip < total)
        {
            var rows = db.Users.OrderBy(u => u.Id).Skip(skip).Take(size).ToList();
            foreach (var row in rows) row.Name = row.Name.Trim();
            db.SaveChanges();
            skip += size;
        }")]
    // Keyset batches until the table runs out.
    [InlineData(@"var lastId = 0;
        while (true)
        {
            var rows = await db.Users.Where(u => u.Id > lastId).OrderBy(u => u.Id).Take(500).ToListAsync();
            if (rows.Count == 0) break;
            lastId = rows[rows.Count - 1].Id;
            foreach (var row in rows) row.Name = row.Name.Trim();
            await db.SaveChangesAsync();
        }")]
    public Task SavePerBatch_NoDiagnostic(string body) => VerifyAsync(body);

    [Theory]
    // A save per item.
    [InlineData(@"foreach (var id in ids)
        {
            var rows = await db.Users.Where(u => u.Id == id).ToListAsync();
            {|LC010:db.SaveChanges()|};
        }")]
    // A counted loop whose query is not a page.
    [InlineData(@"for (var i = 0; i < size; i++)
        {
            var rows = db.Users.Where(u => u.Id == i).ToList();
            {|LC010:db.SaveChanges()|};
        }")]
    // Chunks repeated for every tenant.
    [InlineData(@"foreach (var tenant in tenants)
        {
            foreach (var batch in users.Chunk(1000))
            {
                db.Users.AddRange(batch);
                {|LC010:db.SaveChanges()|};
            }
        }")]
    // A counted loop that walks an array is per item, even with a page-shaped query.
    [InlineData(@"for (var i = 0; i < ids.Length; i++)
        {
            var rows = db.Users.Where(u => u.Id == ids[i]).Skip(i).Take(size).ToList();
            {|LC010:db.SaveChanges()|};
        }")]
    public Task SavePerItem_StillReports(string body) => VerifyAsync(body);
}
