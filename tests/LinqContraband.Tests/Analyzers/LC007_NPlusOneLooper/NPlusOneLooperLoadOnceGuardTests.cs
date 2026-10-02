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
                first = db.Users.Where(u => u.ParentId == 0).FirstOrDefault();
            Console.WriteLine(first.Id + id);
        }")]
    [InlineData(@"User first = null;
        foreach (var id in ids)
        {
            if (first is null)
            {
                first = await db.Users.FindAsync(1);
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
                first = {|LC007:db.Users.Find(id)|};
        }")]
    // The local is reset inside the loop, re-arming the guard on every iteration.
    [InlineData(@"User current = null;
        foreach (var id in ids)
        {
            if (current == null)
                current = {|LC007:db.Users.Find(id)|};
            Console.WriteLine(current.Id);
            current = null;
        }")]
    // The guard checks a different local from the one it assigns.
    [InlineData(@"User first = null;
        User other = null;
        foreach (var id in ids)
        {
            if (other == null)
                first = {|LC007:db.Users.Find(id)|};
        }")]
    // Not a null guard: an inequality check runs the query whenever a value is already loaded.
    [InlineData(@"User first = null;
        foreach (var id in ids)
        {
            if (first != null)
                first = {|LC007:db.Users.Find(id)|};
        }")]
    // An `||` condition can be true while the local is already loaded.
    [InlineData(@"User first = null;
        foreach (var id in ids)
        {
            if (first == null || id > 3)
                first = {|LC007:db.Users.Find(id)|};
        }")]
    // The assignment sits in the else branch of the null check.
    [InlineData(@"User first = null;
        foreach (var id in ids)
        {
            if (first == null)
                Console.WriteLine(id);
            else
                first = {|LC007:db.Users.Find(id)|};
        }")]
    // The guard is declared outside the inner loop but inside the outer one: once per outer item.
    [InlineData(@"foreach (var id in ids)
        {
            User first = null;
            foreach (var other in ids)
            {
                first ??= {|LC007:db.Users.Find(id)|};
            }
        }")]
    // A lambda inside the loop can reset the local between iterations.
    [InlineData(@"User first = null;
        foreach (var id in ids)
        {
            first ??= {|LC007:db.Users.Find(id)|};
            Action reset = () => first = null;
            reset();
        }")]
    public Task LoadOnceGuardThatCanRearm_StillReports(string body) =>
        VerifyCS.VerifyAnalyzerAsync(LoopProgram(body));

    [Fact]
    public async Task LoadOnceGuardedHelperCallInsideLoop_IsQuiet()
    {
        var test = HelperProgram(@"
    async Task Run(List<User> orders)
    {
        User root = null;
        foreach (var order in orders)
        {
            if (root == null)
                root = await GetCustomerAsync(0);
            Console.WriteLine(root.Id + order.Id);
        }
    }

    private Task<User> GetCustomerAsync(int id) => _db.Users.FirstOrDefaultAsync(c => c.Id == id);");

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
