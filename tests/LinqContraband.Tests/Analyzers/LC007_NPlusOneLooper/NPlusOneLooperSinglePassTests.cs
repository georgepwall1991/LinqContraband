using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC007_NPlusOneLooper.NPlusOneLooperAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC007_NPlusOneLooper;

/// <summary>
/// A loop that cannot run its query twice is not N+1: a <c>foreach</c> over a LanguageExt <c>Option&lt;T&gt;</c>
/// (zero or one value, ErsatzTV's idiom for "if present"), a body that always ends in <c>return</c>, <c>throw</c> or
/// <c>break</c>, and a query on the path that leaves the loop. A <c>continue</c> or an outer per-item loop still reports.
/// </summary>
public partial class NPlusOneLooperTests
{
    private const string LanguageExtMock = @"
namespace LanguageExt
{
    public struct Option<A> : IEnumerable<A>
    {
        public IEnumerator<A> GetEnumerator() => null;
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => null;
        public IEnumerable<A> ToSeq() => null;
        public IEnumerable<A> ToList() => null;
        public IEnumerable<A> ToArray() => null;
        public IEnumerable<A> AsEnumerable() => null;
        public IEnumerable<A> Rest() => null;
    }

    public struct Either<L, R>
    {
        public IEnumerable<R> RightToSeq() => null;
        public IEnumerable<L> LeftToSeq() => null;
        public IEnumerable<R> RightToList() => null;
        public IEnumerable<L> LeftToList() => null;
        public IEnumerable<R> RightToArray() => null;
        public IEnumerable<L> LeftToArray() => null;
        public IEnumerable<R> RightAsEnumerable() => null;
        public IEnumerable<L> LeftAsEnumerable() => null;
        public IEnumerable<R> ToSeq() => null;
        public IEnumerable<R> ToList() => null;
        public IEnumerable<R> ToArray() => null;
        public IEnumerable<R> AsEnumerable() => null;
    }

    public struct EitherUnsafe<L, R>
    {
        public IEnumerable<R> RightToSeq() => null;
    }
}

namespace Lookalike
{
    public struct Option<A> : IEnumerable<A>
    {
        public IEnumerator<A> GetEnumerator() => null;
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => null;
        public IEnumerable<A> ToList() => null;
    }

    public struct Either<L, R>
    {
        public IEnumerable<R> RightToSeq() => null;
        public IEnumerable<L> LeftToSeq() => null;
    }

    public struct EitherUnsafe<L, R>
    {
        public IEnumerable<R> RightToSeq() => null;
    }
}
";

    private static string SinglePassProgram(string body) => LoopProgram(body) + LanguageExtMock;

    [Theory]
    [InlineData(@"LanguageExt.Option<int> maybeId = default;
        foreach (var id in maybeId)
        {
            var users = await db.Users.Where(u => u.ParentId == id).ToListAsync();
            Handle(users);
        }")]
    // The sequence form of an Option or Either holds the same zero or one value (ErsatzTV's RightToSeq idiom).
    [InlineData(@"LanguageExt.Either<string, int> maybeId = default;
        foreach (var id in maybeId.RightToSeq())
        {
            Handle(await db.Users.Where(u => u.ParentId == id).ToListAsync());
        }")]
    [InlineData(@"LanguageExt.Either<int, string> maybeId = default;
        foreach (var id in maybeId.LeftToList())
        {
            Handle(await db.Users.Where(u => u.ParentId == id).ToListAsync());
        }")]
    [InlineData(@"LanguageExt.Option<int> maybeId = default;
        foreach (var id in maybeId.ToSeq())
        {
            Handle(await db.Users.Where(u => u.ParentId == id).ToListAsync());
        }")]
    [InlineData(@"foreach (var id in ids)
        {
            var user = await db.Users.FindAsync(id);
            Console.WriteLine(user.Id);
            return;
        }")]
    [InlineData(@"while (count > 0)
        {
            var total = await db.Users.CountAsync();
            Console.WriteLine(total);
            break;
        }")]
    [InlineData(@"foreach (var id in ids)
        {
            if (id > 3)
            {
                Handle(await db.Users.Where(u => u.ParentId == id).ToListAsync());
                return;
            }
        }")]
    [InlineData(@"foreach (var id in ids)
        {
            if (id > 3)
                throw new InvalidOperationException((await db.Users.CountAsync()).ToString());
        }")]
    [InlineData(@"foreach (var id in ids)
        {
            if (id == count)
            {
                var user = await db.Users.FindAsync(id);
                Console.WriteLine(user.Id);
                break;
            }
        }")]
    public Task QueryThatRunsAtMostOncePerLoop_IsQuiet(string body) =>
        VerifyCS.VerifyAnalyzerAsync(SinglePassProgram(body));

    [Theory]
    // An Option inside a real per-item loop still runs once per item.
    [InlineData(@"LanguageExt.Option<int> maybeId = default;
        foreach (var id in ids)
        {
            foreach (var parent in maybeId)
            {
                Handle(await {|LC007:db.Users.Where(u => u.ParentId == parent + id).ToListAsync()|});
            }
        }")]
    // Only LanguageExt's Option is known to hold at most one value.
    [InlineData(@"Lookalike.Option<int> maybeId = default;
        foreach (var id in maybeId)
        {
            Handle(await {|LC007:db.Users.Where(u => u.ParentId == id).ToListAsync()|});
        }")]
    // Only LanguageExt's Either, and only its known single-value projections.
    [InlineData(@"Lookalike.Either<string, int> maybeId = default;
        foreach (var id in maybeId.RightToSeq())
        {
            Handle(await {|LC007:db.Users.Where(u => u.ParentId == id).ToListAsync()|});
        }")]
    [InlineData(@"LanguageExt.Option<int> maybeId = default;
        foreach (var id in maybeId.Rest())
        {
            Handle(await {|LC007:db.Users.Where(u => u.ParentId == id).ToListAsync()|});
        }")]
    // A continue before the final return starts another pass.
    [InlineData(@"foreach (var id in ids)
        {
            var user = await {|LC007:db.Users.FindAsync(id)|};
            if (user == null)
                continue;
            return;
        }")]
    // The break leaves the inner switch, not the loop.
    [InlineData(@"foreach (var id in ids)
        {
            switch (id)
            {
                case 1:
                    Console.WriteLine(await {|LC007:db.Users.CountAsync()|});
                    break;
            }
        }")]
    // A return in a nested lambda does not leave the loop.
    [InlineData(@"foreach (var id in ids)
        {
            var total = await {|LC007:db.Users.CountAsync()|};
            Func<int> read = () => { return total; };
            Console.WriteLine(read());
        }")]
    // The query runs on every pass of the inner loop before the outer return.
    [InlineData(@"foreach (var id in ids)
        {
            for (var i = 0; i < count; i++)
            {
                Console.WriteLine(await {|LC007:db.Users.CountAsync()|});
            }
            return;
        }")]
    // The exit is in a branch the query's path does not take.
    [InlineData(@"foreach (var id in ids)
        {
            var user = await {|LC007:db.Users.FindAsync(id)|};
            if (user == null)
                return;
        }")]
    // The statement that runs the query can continue before the break.
    [InlineData(@"foreach (var id in ids)
        {
            if (await {|LC007:db.Users.Where(u => u.Id == id).AnyAsync()|})
                continue;
            break;
        }")]
    // A catch inside the loop handles the throw, so the next item runs the query again.
    [InlineData(@"foreach (var id in ids)
        {
            try
            {
                var user = await {|LC007:db.Users.FindAsync(id)|};
                throw new InvalidOperationException(user.Id.ToString());
            }
            catch (InvalidOperationException)
            {
            }
        }")]
    // A conditional throw before the break lands in the catch, and the loop carries on.
    [InlineData(@"foreach (var id in ids)
        {
            try
            {
                if (id > 0)
                {
                    var user = await {|LC007:db.Users.FindAsync(id)|};
                    if (id == 1)
                        throw new InvalidOperationException(user.Id.ToString());
                    break;
                }
            }
            catch (InvalidOperationException)
            {
            }
        }")]
    // A switch break before the return leaves the case, not the loop.
    [InlineData(@"foreach (var id in ids)
        {
            switch (id)
            {
                case 1:
                {
                    Console.WriteLine(await {|LC007:db.Users.CountAsync()|});
                    if (count > 0)
                        break;
                    return;
                }
            }
        }")]
    public Task QueryThatCanRunAgain_StillReports(string body) =>
        VerifyCS.VerifyAnalyzerAsync(SinglePassProgram(body));
}
