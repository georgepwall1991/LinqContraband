using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC010_SaveChangesInLoop.SaveChangesInLoopAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC010_SaveChangesInLoop;

/// <summary>
/// A save that cannot run twice in the loop is one save: a <c>foreach</c> over a LanguageExt <c>Option&lt;T&gt;</c>,
/// a body that always ends in <c>return</c>/<c>throw</c>/<c>break</c>, or a save followed by an exit
/// (<c>SaveChanges(); return;</c>). ErsatzTV reported 23 of these.
/// </summary>
public class SaveChangesInLoopSinglePassTests
{
    private static string Program(string body) => @"
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

class Program
{
    async Task<string> Run(AppDbContext db, List<int> ids, LanguageExt.Option<int> maybeId, Lookalike.Option<int> otherId)
    {
        " + body + @"
        return null;
    }
}

class AppDbContext : DbContext { }

namespace Microsoft.EntityFrameworkCore
{
    public class DbContext : IDisposable
    {
        public int SaveChanges() => 0;
        public Task<int> SaveChangesAsync() => Task.FromResult(0);
        public void Dispose() { }
    }
}

namespace LanguageExt
{
    public struct Option<A> : IEnumerable<A>
    {
        public IEnumerator<A> GetEnumerator() => null;
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => null;
    }
}

namespace Lookalike
{
    public struct Option<A> : IEnumerable<A>
    {
        public IEnumerator<A> GetEnumerator() => null;
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => null;
    }
}
";

    [Theory]
    [InlineData(@"foreach (var id in maybeId)
        {
            Console.WriteLine(id);
            await db.SaveChangesAsync();
        }")]
    [InlineData(@"foreach (var id in ids)
        {
            if (id < 0)
                return ""negative"";
            db.SaveChanges();
            return null;
        }")]
    [InlineData(@"foreach (var id in ids)
        {
            if (id == 3)
            {
                await db.SaveChangesAsync();
                return ""saved"";
            }
        }")]
    [InlineData(@"foreach (var id in ids)
        {
            if (id == 3)
            {
                db.SaveChanges();
                break;
            }
        }")]
    public Task SaveThatRunsAtMostOnce_IsQuiet(string body) =>
        VerifyCS.VerifyAnalyzerAsync(Program(body));

    [Theory]
    [InlineData(@"foreach (var id in otherId)
        {
            {|LC010:db.SaveChanges()|};
        }")]
    [InlineData(@"foreach (var id in ids)
        {
            foreach (var other in maybeId)
            {
                {|LC010:db.SaveChanges()|};
            }
        }")]
    [InlineData(@"foreach (var id in ids)
        {
            {|LC010:db.SaveChanges()|};
            if (id > 0)
                continue;
            return null;
        }")]
    [InlineData(@"foreach (var id in ids)
        {
            try
            {
                {|LC010:db.SaveChanges()|};
            }
            catch (InvalidOperationException)
            {
                return ""failed"";
            }
        }")]
    [InlineData(@"foreach (var id in ids)
        {
            if ({|LC010:db.SaveChanges()|} > 0)
                continue;
            break;
        }")]
    [InlineData(@"foreach (var id in ids)
        {
            try
            {
                {|LC010:db.SaveChanges()|};
                throw new InvalidOperationException();
            }
            catch (InvalidOperationException)
            {
            }
        }")]
    public Task SaveThatCanRunAgain_StillReports(string body) =>
        VerifyCS.VerifyAnalyzerAsync(Program(body));
}
