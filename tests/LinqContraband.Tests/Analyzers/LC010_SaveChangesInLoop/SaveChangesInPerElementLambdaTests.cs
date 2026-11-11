using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using LinqContraband.Analyzers.LC010_SaveChangesInLoop;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC010_SaveChangesInLoop.SaveChangesInLoopAnalyzer>;
using VerifyFix = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.CodeFixVerifier<
    LinqContraband.Analyzers.LC010_SaveChangesInLoop.SaveChangesInLoopAnalyzer,
    LinqContraband.Analyzers.LC010_SaveChangesInLoop.SaveChangesInLoopFixer>;

namespace LinqContraband.Tests.Analyzers.LC010_SaveChangesInLoop;

/// <summary>
/// A save inside a lambda that an in-memory operator runs once per element (<c>items.ForEach(...)</c>,
/// <c>items.Select(...)</c>, <c>Parallel.ForEach(...)</c>) is a save per item. LC010 reports it unless each
/// element has a context of its own, or the elements are <c>Chunk</c> batches.
/// </summary>
public class SaveChangesInPerElementLambdaTests
{
    private const string Mock = @"
namespace Microsoft.EntityFrameworkCore
{
    public class DbContext : IDisposable
    {
        public int SaveChanges() => 0;
        public Task<int> SaveChangesAsync() => Task.FromResult(0);
        public void Dispose() {}
    }
}

public class MyDbContext : Microsoft.EntityFrameworkCore.DbContext
{
}
";

    private static string Program(string body) => @"
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

class Program
{
    private readonly MyDbContext db = new MyDbContext();

    async Task Run(List<int> items, List<MyDbContext> contexts)
    {
" + body + @"
        await Task.CompletedTask;
    }
}
" + Mock;

    [Theory]
    [InlineData("items.ForEach(item => { {|LC010:db.SaveChanges()|}; });")]
    [InlineData("var written = items.Select(item => {|LC010:db.SaveChanges()|}).ToList();")]
    [InlineData("await Task.WhenAll(items.Select(item => {|LC010:db.SaveChangesAsync()|}));")]
    [InlineData("await Task.WhenAll(items.Select(async item => await {|LC010:db.SaveChangesAsync()|}));")]
    [InlineData("Parallel.ForEach(items, item => { {|LC010:db.SaveChanges()|}; });")]
    public Task SaveInPerElementLambda_Reports(string body) =>
        VerifyCS.VerifyAnalyzerAsync(Program(body));

    [Theory]
    // Each element saves a context of its own.
    [InlineData("items.ForEach(item => { using var fresh = new MyDbContext(); fresh.SaveChanges(); });")]
    [InlineData("contexts.ForEach(context => context.SaveChanges());")]
    // Not per element.
    [InlineData("var saved = await Task.Run(() => db.SaveChanges());")]
    [InlineData("Func<int, int> save = item => db.SaveChanges();")]
    [InlineData("var total = items.Aggregate(0, (sum, item) => sum + item, sum => db.SaveChanges());")]
    public Task SaveInLambda_OwnContextOrNotPerElement_IsQuiet(string body) =>
        VerifyCS.VerifyAnalyzerAsync(Program(body));

    [Fact]
    public async Task SaveInPerElementLambda_OverChunkBatches_IsQuiet()
    {
        var test = new CSharpAnalyzerTest<SaveChangesInLoopAnalyzer, DefaultVerifier>
        {
            TestCode = Program("items.Chunk(500).ToList().ForEach(batch => { db.SaveChanges(); });"),
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };

        await test.RunAsync();
    }

    [Fact]
    public async Task SaveInPerElementLambda_HasNoCodeFix()
    {
        var test = Program("items.ForEach(item => { {|LC010:db.SaveChanges()|}; });");
        await VerifyFix.VerifyCodeFixAsync(test, test);
    }
}
