using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC031_UnboundedQueryMaterialization.UnboundedQueryMaterializationAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC031_UnboundedQueryMaterialization;

public class UnboundedQueryMaterializationTests
{
    private const string Usings = @"
using System;
using System.Linq;
using System.Linq.Expressions;
using System.Collections;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
";

    private const string EFCoreMock = @"
namespace Microsoft.EntityFrameworkCore
{
    public class DbSet<TEntity> : IQueryable<TEntity> where TEntity : class
    {
        public Type ElementType => typeof(TEntity);
        public Expression Expression => null;
        public IQueryProvider Provider => null;
        public IEnumerator<TEntity> GetEnumerator() => null;
        IEnumerator IEnumerable.GetEnumerator() => null;
    }

    public class DbContext
    {
    }
}
";

    private const string Entities = @"
namespace TestApp
{
    public class User { public int Id { get; set; } public bool IsActive { get; set; } }

    public class AppDbContext : Microsoft.EntityFrameworkCore.DbContext
    {
        public Microsoft.EntityFrameworkCore.DbSet<User> Users { get; set; }
    }
}
";

    [Fact]
    public async Task ToList_FromDbSet_WithoutBounding_ShouldTriggerLC031()
    {
        var test = Usings + EFCoreMock + Entities + @"
namespace TestApp
{
    public class TestClass
    {
        public void TestMethod(AppDbContext db)
        {
            var result = {|LC031:db.Users.Where(u => u.IsActive).ToList()|};
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ToList_FromDbSet_NoPredicate_ShouldTriggerLC031()
    {
        var test = Usings + EFCoreMock + Entities + @"
namespace TestApp
{
    public class TestClass
    {
        public void TestMethod(AppDbContext db)
        {
            var result = {|LC031:db.Users.ToList()|};
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ToList_WithTake_ShouldNotTrigger()
    {
        var test = Usings + EFCoreMock + Entities + @"
namespace TestApp
{
    public class TestClass
    {
        public void TestMethod(AppDbContext db)
        {
            var result = db.Users.Take(100).ToList();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task FirstOrDefault_ShouldNotTrigger()
    {
        var test = Usings + EFCoreMock + Entities + @"
namespace TestApp
{
    public class TestClass
    {
        public void TestMethod(AppDbContext db)
        {
            var result = db.Users.Where(u => u.IsActive).FirstOrDefault();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ToList_OnNonDbSet_ShouldNotTrigger()
    {
        var test = @"
using System.Linq;
using System.Collections.Generic;

namespace TestApp
{
    public class User { public bool IsActive { get; set; } }

    public class TestClass
    {
        public void TestMethod(List<User> users)
        {
            var result = users.Where(u => u.IsActive).ToList();
        }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
