using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC007_NPlusOneLooper.NPlusOneLooperAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC007_NPlusOneLooper;

/// <summary>
/// A load-once guard (<c>if (x == null) x = query;</c> or <c>x ??= query;</c> on a local declared before the loop)
/// runs the query at most once, so it is not N+1. Guards that can re-arm inside the loop still report.
/// </summary>
public partial class NPlusOneLooperTests
{
    [Theory]
    [InlineData(@"User first = null;
        foreach (var id in ids)
        {
            if (first == null)
                first = db.Users.Where(u => u.ParentId == 0).First();
            Console.WriteLine(first.Id + id);
        }")]
    [InlineData(@"User first = null;
        foreach (var id in ids)
        {
            if (first is null)
            {
                first = db.Users.First(u => u.Id == 1);
            }
            Console.WriteLine(first.Id + id);
        }")]
    [InlineData(@"List<User> all = null;
        foreach (var id in ids)
        {
            all ??= await db.Users.ToListAsync();
            Console.WriteLine(all.Count + id);
        }")]
    [InlineData(@"int? total = null;
        for (var i = 0; i < count; i++)
        {
            if (total == null && i > 0)
                total = await db.Users.CountAsync();
        }")]
    [InlineData(@"Dictionary<int, User> lookup = null;
        foreach (var id in ids)
        {
            lookup ??= (await db.Users.ToListAsync()).ToDictionary(u => u.Id);
            Console.WriteLine(lookup.Count + id);
        }")]
    public Task LoadOnceGuardedQueryInsideLoop_IsQuiet(string body) =>
        VerifyCS.VerifyAnalyzerAsync(LoopProgram(body));

    [Theory]
    // The guarded local is declared inside the loop, so the guard is always true.
    [InlineData(@"foreach (var id in ids)
        {
            User first = null;
            if (first == null)
                first = {|LC007:db.Users.First(u => u.Id == id)|};
        }")]
    // The local is reset inside the loop, re-arming the guard on every iteration.
    [InlineData(@"User current = null;
        foreach (var id in ids)
        {
            if (current == null)
                current = {|LC007:db.Users.First(u => u.Id == id)|};
            Console.WriteLine(current.Id);
            current = null;
        }")]
    // The guard checks a different local from the one it assigns.
    [InlineData(@"User first = null;
        User other = null;
        foreach (var id in ids)
        {
            if (other == null)
                first = {|LC007:db.Users.First(u => u.Id == id)|};
        }")]
    // Not a null guard: an inequality check runs the query whenever a value is already loaded.
    [InlineData(@"User first = null;
        foreach (var id in ids)
        {
            if (first != null)
                first = {|LC007:db.Users.First(u => u.Id == id)|};
        }")]
    // An `||` condition can be true while the local is already loaded.
    [InlineData(@"User first = null;
        foreach (var id in ids)
        {
            if (first == null || id > 3)
                first = {|LC007:db.Users.First(u => u.Id == id)|};
        }")]
    // The assignment sits in the else branch of the null check.
    [InlineData(@"User first = null;
        foreach (var id in ids)
        {
            if (first == null)
                Console.WriteLine(id);
            else
                first = {|LC007:db.Users.First(u => u.Id == id)|};
        }")]
    // The guard is declared outside the inner loop but inside the outer one: once per outer item.
    [InlineData(@"foreach (var id in ids)
        {
            User first = null;
            foreach (var other in ids)
            {
                first ??= {|LC007:db.Users.First(u => u.Id == id)|};
            }
        }")]
    // A lambda inside the loop can reset the local between iterations.
    [InlineData(@"User first = null;
        foreach (var id in ids)
        {
            first ??= {|LC007:db.Users.First(u => u.Id == id)|};
            Action reset = () => first = null;
            reset();
        }")]
    // A query that throws inside a try whose catch lets the loop go on leaves the local null for the next iteration.
    [InlineData(@"User first = null;
        foreach (var id in ids)
        {
            try
            {
                first ??= {|LC007:db.Users.First(u => u.Id == id)|};
            }
            catch (InvalidOperationException)
            {
            }
        }")]
    // An `as` cast can turn the non-null row into null, leaving the guard open.
    [InlineData(@"IDisposable cached = null;
        foreach (var id in ids)
        {
            cached ??= {|LC007:db.Users.First(u => u.Id == id)|} as IDisposable;
        }")]
    public Task LoadOnceGuardThatCanRearm_StillReports(string body) =>
        VerifyCS.VerifyAnalyzerAsync(LoopProgram(body));

    [Fact]
    public Task LoadOnceGuardWithUserDefinedEquality_StillReports()
    {
        // A user-defined == can report a loaded value as null, so the guard does not prove the query runs once.
        var test = Usings + @"
class Holder
{
    public Holder(User user) { }
    public static bool operator ==(Holder left, Holder right) => true;
    public static bool operator !=(Holder left, Holder right) => false;
    public override bool Equals(object obj) => true;
    public override int GetHashCode() => 0;
}

class Program
{
    void Run(MyDbContext db, int[] ids)
    {
        Holder holder = null;
        foreach (var id in ids)
        {
            if (holder == null)
                holder = new Holder({|LC007:db.Users.First(u => u.Id == id)|});
        }
    }
}" + MockNamespace;
        return VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public Task LoadOnceGuardWithRefAlias_StillReports()
    {
        // A ref alias can reset the local under another name.
        var test = Usings + @"
class Program
{
    void Run(MyDbContext db, int[] ids)
    {
        User first = null;
        ref var alias = ref first;
        foreach (var id in ids)
        {
            first ??= {|LC007:db.Users.First(u => u.Id == id)|};
            alias = null;
        }
    }
}" + MockNamespace;
        return VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Theory]
    // FirstOrDefault, Find and their async forms leave the local null when nothing matches, so the guard stays open
    // and the query runs again on the next iteration.
    [InlineData(@"User first = null;
        foreach (var id in ids)
        {
            if (first == null)
                first = {|LC007:db.Users.Where(u => u.ParentId == id).FirstOrDefault()|};
        }")]
    [InlineData(@"User first = null;
        foreach (var id in ids)
        {
            if (first is null)
            {
                first = await {|LC007:db.Users.FindAsync(id)|};
            }
        }")]
    [InlineData(@"User first = null;
        foreach (var id in ids)
        {
            first ??= {|LC007:db.Users.Find(id)|};
        }")]
    public Task LoadOnceGuardWhoseQueryCanReturnNull_StillReports(string body) =>
        VerifyCS.VerifyAnalyzerAsync(LoopProgram(body));

    [Fact]
    public async Task LoadOnceGuardedHelperCallInsideLoop_IsQuiet()
    {
        var test = HelperProgram(@"
#nullable enable
    async Task Run(List<User> orders)
    {
        User? root = null;
        foreach (var order in orders)
        {
            if (root == null)
                root = await GetCustomerAsync(0);
            Console.WriteLine(root.Id + order.Id);
        }
    }

    private Task<User> GetCustomerAsync(int id) => Task.FromResult(_db.Users.First(c => c.Id == id));
#nullable restore");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Theory]
    // The helper may return null (annotated, or written without nullable annotations), so the guard can stay open.
    [InlineData(@"#nullable enable
    private Task<User?> GetCustomerAsync(int id) => _db.Users.FirstOrDefaultAsync(c => c.Id == id)!;
#nullable restore", "FirstOrDefaultAsync")]
    [InlineData(@"private Task<User> GetCustomerAsync(int id) => Task.FromResult(_db.Users.First(c => c.Id == id));", "First")]
    public async Task LoadOnceGuardedHelperThatCanReturnNull_StillReports(string helper, string query)
    {
        var test = HelperProgram(@"
    async Task Run(List<User> orders)
    {
        User root = null;
        foreach (var order in orders)
        {
            if (root == null)
                root = await {|#0:GetCustomerAsync(0)|};
        }
    }

    " + helper);

        await VerifyCS.VerifyAnalyzerAsync(test, HelperCall(0, "GetCustomerAsync", query));
    }
}
