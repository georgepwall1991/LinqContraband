using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    LinqContraband.Analyzers.LC031_UnboundedQueryMaterialization.UnboundedQueryMaterializationAnalyzer>;

namespace LinqContraband.Tests.Analyzers.LC031_UnboundedQueryMaterialization;

/// <summary>
/// A <c>ToList()</c> inside an expression-tree lambda (a projection, or any <c>Expression&lt;Func&lt;...&gt;&gt;</c>) is
/// part of the query EF Core translates, not a materialization of its own. LC031 judges only the outer terminal.
/// </summary>
public partial class UnboundedQueryMaterializationTests
{
    private static string ExpressionTreeProgram(string body) => Usings + EFCoreMock + @"
namespace Microsoft.EntityFrameworkCore
{
    // A model-building API whose expression is compiled and run in .NET, not translated.
    public static class ConversionBuilderExtensions
    {
        public static void HasConversion<T>(this object builder, Expression<Func<T, object>> convert) { }
    }
}

namespace TestApp
{
    public class Post { public int Id { get; set; } public int BlogId { get; set; } public string Title { get; set; } }

    public class Blog { public int Id { get; set; } public string Name { get; set; } public List<Post> Posts { get; set; } }

    public class BlogDto { public string Name { get; set; } public List<string> Titles { get; set; } public Post[] Posts { get; set; } }

    public class AppDbContext : Microsoft.EntityFrameworkCore.DbContext
    {
        public Microsoft.EntityFrameworkCore.DbSet<Blog> Blogs { get; set; }
        public Microsoft.EntityFrameworkCore.DbSet<Post> Posts { get; set; }
    }

    public class TestClass
    {
        public object Run(AppDbContext db)
        {
            " + body + @"
        }

        private static T Execute<T>(Expression<Func<T>> query) => query.Compile()();
    }
}";

    [Theory]
    // Navigation collection projected with a nested ToList, outer terminal bounded.
    [InlineData("return db.Blogs.Select(b => new BlogDto { Titles = b.Posts.Select(p => p.Title).ToList() }).Take(10).ToList();")]
    // Correlated DbSet subquery inside the projection, outer terminal bounded.
    [InlineData("return db.Blogs.Select(b => new BlogDto { Posts = db.Posts.Where(p => p.BlogId == b.Id).ToArray() }).Take(10).ToList();")]
    [InlineData("return db.Blogs.Select(b => new { b.Name, Posts = db.Set<Post>().Where(p => p.BlogId == b.Id).ToList() }).Take(10).ToList();")]
    // Query syntax projection.
    [InlineData("return (from b in db.Blogs select new { b.Name, Posts = db.Posts.Where(p => p.BlogId == b.Id).ToList() }).Take(10).ToList();")]
    // The query source reached through a local or DbContext.Set<T>().
    [InlineData("var blogs = db.Blogs.Where(b => b.Id > 0); return blogs.Select(b => new BlogDto { Posts = db.Posts.Where(p => p.BlogId == b.Id).ToArray() }).Take(10).ToList();")]
    public Task NestedMaterializerInsideExpressionTree_IsQuiet(string body) =>
        VerifyCS.VerifyAnalyzerAsync(ExpressionTreeProgram(body));

    [Theory]
    // The outer terminal still reports when it is unbounded; the nested one does not.
    [InlineData("return {|LC031:db.Blogs.Select(b => new BlogDto { Posts = db.Posts.Where(p => p.BlogId == b.Id).ToArray() }).ToList()|};")]
    [InlineData("return {|LC031:db.Blogs.Select(b => new BlogDto { Titles = b.Posts.Select(p => p.Title).ToList() }).ToList()|};")]
    [InlineData("return {|LC031:(from b in db.Blogs select new { b.Name, Posts = db.Posts.Where(p => p.BlogId == b.Id).ToList() }).ToList()|};")]
    // A delegate lambda (not an expression tree) runs the query itself.
    [InlineData("Func<Blog, List<Post>> load = b => {|LC031:db.Posts.Where(p => p.BlogId == b.Id).ToList()|}; return load;")]
    // An expression tree held in a local or passed to a non-query method can be compiled and run in memory.
    [InlineData("Expression<Func<List<Post>>> load = () => {|LC031:db.Posts.ToList()|}; return load.Compile()();")]
    [InlineData("Expression<Func<Blog, List<Post>>> selector = b => {|LC031:db.Posts.Where(p => p.BlogId == b.Id).ToList()|}; return selector;")]
    [InlineData("return Execute(() => {|LC031:db.Posts.ToList()|});")]
    [InlineData("new object().HasConversion<int>(id => {|LC031:db.Posts.ToList()|}); return null;")]
    // AsQueryable() over an in-memory sequence compiles the selector and runs it locally.
    [InlineData("return new[] { 1 }.AsQueryable().Select(_ => {|LC031:db.Posts.ToList()|}).ToList();")]
    public Task OuterUnboundedTerminalOrDelegateLambda_StillReports(string body) =>
        VerifyCS.VerifyAnalyzerAsync(ExpressionTreeProgram(body));
}
