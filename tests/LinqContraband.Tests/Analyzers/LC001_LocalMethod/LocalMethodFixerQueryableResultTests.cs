using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.CodeFixVerifier<
    LinqContraband.Analyzers.LC001_LocalMethod.LocalMethodAnalyzer,
    LinqContraband.Analyzers.LC001_LocalMethod.LocalMethodFixer>;

namespace LinqContraband.Tests.Analyzers.LC001_LocalMethod;

// AsEnumerable() turns the query into an IEnumerable<T>. These shapes still need an IQueryable<T> after the fix,
// so the fix did not compile on real projects (BTCPay Server, Kavita); LC001 now reports them without a fix.
public class LocalMethodFixerQueryableResultTests
{
    private const string Header = @"
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TestNamespace;
";

    private const string MockNamespace = @"
namespace TestNamespace
{
    public class Role
    {
        public int Level { get; set; }
    }

    public class User
    {
        public int Age { get; set; }
        public List<Role> Roles { get; set; } = new List<Role>();
    }

    public class DbContext
    {
        public IQueryable<User> Users => new List<User>().AsQueryable();
    }

    public static class AsyncQueryableExtensions
    {
        public static Task<T[]> ToArrayAsync<T>(this IQueryable<T> source) => Task.FromResult(source.ToArray());
    }

    public static class RoleExtensions
    {
        public static IQueryable<Role> Restrict(this IQueryable<Role> roles, int level) => roles.Where(r => r.Level <= level);
    }
}";

    private static Task VerifyNoFixAsync(string body)
    {
        var test = Header + body + MockNamespace;
        return VerifyCS.VerifyCodeFixAsync(test, test);
    }

    [Fact]
    public async Task SelfAssignedQueryableLocal_ReportsWithoutFix()
    {
        await VerifyNoFixAsync(@"
class Program
{
    IQueryable<User> Filter(IQueryable<User> query)
    {
        query = query.Where(u => {|LC001:IsAdult(u.Age)|});
        return query;
    }

    bool IsAdult(int age) => age >= 18;
}
");
    }

    [Fact]
    public async Task AsyncQueryableTerminal_ReportsWithoutFix()
    {
        await VerifyNoFixAsync(@"
class Program
{
    async Task<User[]> Load(DbContext db)
    {
        return await db.Users.Where(u => {|LC001:IsAdult(u.Age)|}).Select(u => u).ToArrayAsync();
    }

    bool IsAdult(int age) => age >= 18;
}
");
    }

    [Fact]
    public async Task QueryPassedAsQueryableArgument_ReportsWithoutFix()
    {
        await VerifyNoFixAsync(@"
class Program
{
    int Run(DbContext db) => Page(db.Users.Where(u => {|LC001:IsAdult(u.Age)|}));

    int Page(IQueryable<User> query) => query.Count();
    bool IsAdult(int age) => age >= 18;
}
");
    }

    [Fact]
    public async Task QueryReturnedAsQueryable_ReportsWithoutFix()
    {
        await VerifyNoFixAsync(@"
class Program
{
    IQueryable<User> Adults(DbContext db) => db.Users.Where(u => {|LC001:IsAdult(u.Age)|});

    bool IsAdult(int age) => age >= 18;
}
");
    }

    [Fact]
    public async Task VarLocalLaterUsedAsQueryable_ReportsWithoutFix()
    {
        await VerifyNoFixAsync(@"
class Program
{
    async Task<int> Run(DbContext db)
    {
        var query = db.Users.Where(u => {|LC001:IsAdult(u.Age)|});
        var rows = await query.ToArrayAsync();
        return rows.Length;
    }

    bool IsAdult(int age) => age >= 18;
}
");
    }

    [Fact]
    public async Task LocalMethodOnRowSubQuery_ReportsWithoutFix()
    {
        await VerifyNoFixAsync(@"
class Program
{
    List<int> Run(DbContext db, int level)
    {
        return db.Users
            .Select(u => {|LC001:u.Roles.AsQueryable().Where(r => r.Level > 0).Restrict(level)|}.Count())
            .OrderBy(count => count)
            .ToList();
    }
}
");
    }

    [Fact]
    public async Task VarLocalUsedOnlyAsSequence_IsStillFixed()
    {
        var test = Header + @"
class Program
{
    int Run(DbContext db)
    {
        var query = db.Users.Where(u => {|LC001:IsAdult(u.Age)|});
        var total = 0;
        foreach (var user in query) total += user.Age;
        return query.Count() + total;
    }

    bool IsAdult(int age) => age >= 18;
}
" + MockNamespace;

        var fixedCode = Header + @"
class Program
{
    int Run(DbContext db)
    {
        var query = db.Users.AsEnumerable().Where(u => IsAdult(u.Age));
        var total = 0;
        foreach (var user in query) total += user.Age;
        return query.Count() + total;
    }

    bool IsAdult(int age) => age >= 18;
}
" + MockNamespace;

        await VerifyCS.VerifyCodeFixAsync(test, fixedCode);
    }

    [Fact]
    public async Task ChainEndingInToList_IsStillFixed()
    {
        var test = Header + @"
class Program
{
    List<User> Run(DbContext db) => db.Users.Where(u => {|LC001:IsAdult(u.Age)|}).OrderBy(u => u.Age).ToList();

    bool IsAdult(int age) => age >= 18;
}
" + MockNamespace;

        var fixedCode = Header + @"
class Program
{
    List<User> Run(DbContext db) => db.Users.AsEnumerable().Where(u => IsAdult(u.Age)).OrderBy(u => u.Age).ToList();

    bool IsAdult(int age) => age >= 18;
}
" + MockNamespace;

        await VerifyCS.VerifyCodeFixAsync(test, fixedCode);
    }
}
