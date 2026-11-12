using LinqContraband.Analyzers.LC010_SaveChangesInLoop;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
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
    async Task<string> Run(AppDbContext db, List<int> ids, LanguageExt.Option<int> maybeId, Lookalike.Option<int> otherId, LanguageExt.Either<string, int> eitherId, Lookalike.Either<string, int> otherEither)
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
        public IEnumerable<A> ToSeq() => null;
        public IEnumerable<A> Rest() => null;
    }

    public struct Either<L, R>
    {
        public IEnumerable<R> RightToSeq() => null;
        public IEnumerable<L> LeftToList() => null;
    }
}

namespace Lookalike
{
    public struct Option<A> : IEnumerable<A>
    {
        public IEnumerator<A> GetEnumerator() => null;
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => null;
    }

    public struct Either<L, R>
    {
        public IEnumerable<R> RightToSeq() => null;
    }
}
";

    [Theory]
    [InlineData(@"foreach (var id in maybeId)
        {
            Console.WriteLine(id);
            await db.SaveChangesAsync();
        }")]
    [InlineData(@"foreach (var id in eitherId.RightToSeq())
        {
            await db.SaveChangesAsync();
        }")]
    [InlineData(@"foreach (var id in maybeId.ToSeq())
        {
            db.SaveChanges();
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
    [InlineData(@"foreach (var id in otherEither.RightToSeq())
        {
            {|LC010:db.SaveChanges()|};
        }")]
    [InlineData(@"foreach (var id in maybeId.Rest())
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
    [InlineData(@"foreach (var id in ids)
        {
            try
            {
                if (id > 0)
                {
                    {|LC010:db.SaveChanges()|};
                    if (id == 1)
                        throw new InvalidOperationException();
                    break;
                }
            }
            catch (InvalidOperationException)
            {
            }
        }")]
    [InlineData(@"foreach (var id in ids)
        {
            switch (id)
            {
                case 1:
                {
                    {|LC010:db.SaveChanges()|};
                    if (id > 0)
                        break;
                    return null;
                }
            }
        }")]
    public Task SaveThatCanRunAgain_StillReports(string body) =>
        VerifyCS.VerifyAnalyzerAsync(Program(body));
    [Theory]
    // A list written in the source, or the values of an enum: the code fixes the number of saves.
    [InlineData(@"foreach (var id in new[] { 1, 2, 3 })
        {
            db.SaveChanges();
        }")]
    [InlineData(@"foreach (var day in Enum.GetValues(typeof(DayOfWeek)))
        {
            await db.SaveChangesAsync();
        }")]
    [InlineData(@"var wanted = new[] { 1, 2, 3 };
        foreach (var id in wanted)
        {
            db.SaveChanges();
        }")]
    public Task SaveInALoopOverAListWrittenInTheSource_IsQuiet(string body) =>
        VerifyCS.VerifyAnalyzerAsync(Program(body));

    [Theory]
    [InlineData(@"foreach (var day in Enum.GetValues<DayOfWeek>())
        {
            await db.SaveChangesAsync();
        }")]
    [InlineData(@"foreach (var name in Enum.GetNames<DayOfWeek>())
        {
            db.SaveChanges();
        }")]
    public Task SaveInALoopOverGenericEnumValues_IsQuiet(string body) =>
        VerifySaveOnNet80Async(body);

    [Fact]
    public Task SaveInALoopOverAListHeldInALocal_StillReports() =>
        VerifyCS.VerifyAnalyzerAsync(Program(@"var wanted = new List<int> { 1, 2, 3 };
        foreach (var id in wanted)
        {
            {|LC010:db.SaveChanges()|};
        }"));

    [Theory]
    [InlineData(@"foreach (var id in ids)
        {
            foreach (var attempt in new[] { 1, 2 })
            {
                {|LC010:db.SaveChanges()|};
            }
        }")]
    [InlineData(@"foreach (var id in new List<int>(ids) { 0 })
        {
            {|LC010:db.SaveChanges()|};
        }")]
    public Task SaveInAFixedListInsideAPerItemLoop_StillReports(string body) =>
        VerifyCS.VerifyAnalyzerAsync(Program(body));

    // Enum.GetValues<T>() / GetNames<T>() are .NET 5+. The default VerifyCS
    // references do not declare them, so those shapes compile as an error
    // (MissingMethodException = diagnostic/compile mismatch) unless we raise
    // the harness to Net80.
    private static Task VerifySaveOnNet80Async(string body)
    {
        var test = new CSharpAnalyzerTest<SaveChangesInLoopAnalyzer, DefaultVerifier>
        {
            TestCode = Program(body),
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };

        return test.RunAsync();
    }
}
