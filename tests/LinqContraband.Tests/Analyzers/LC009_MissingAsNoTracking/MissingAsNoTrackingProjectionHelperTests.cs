using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.CodeFixVerifier<
    LinqContraband.Analyzers.LC009_MissingAsNoTracking.MissingAsNoTrackingAnalyzer,
    LinqContraband.Analyzers.LC009_MissingAsNoTracking.MissingAsNoTrackingFixer>;

namespace LinqContraband.Tests.Analyzers.LC009_MissingAsNoTracking;

/// <summary>
/// AutoMapper's <c>ProjectTo&lt;Dto&gt;()</c> and hand-written extensions that select IDs or DTOs reshape a DbSet
/// query without a visible <c>Select</c>. EF Core does not track what they return, so LC009 stays quiet. Helpers
/// that still return entities (the root entity, a derived type, a navigation or another DbSet's entity) keep
/// reporting.
/// </summary>
public class MissingAsNoTrackingProjectionHelperTests
{
    private const string Mock = @"
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TestNamespace;

namespace Microsoft.EntityFrameworkCore
{
    public class DbContext
    {
        public int SaveChanges() => 0;
        public DbSet<T> Set<T>() where T : class => null;
    }

    public class DbSet<T> : IQueryable<T> where T : class
    {
        public Type ElementType => typeof(T);
        public System.Linq.Expressions.Expression Expression => System.Linq.Expressions.Expression.Constant(this);
        public IQueryProvider Provider => null;
        public IEnumerator<T> GetEnumerator() => null;
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => null;
    }

    public static class EntityFrameworkQueryableExtensions
    {
        public static IQueryable<T> AsNoTracking<T>(this IQueryable<T> source) => source;
        public static Task<List<T>> ToListAsync<T>(this IQueryable<T> source) => Task.FromResult(new List<T>());
    }
}

namespace TestNamespace
{
    public interface IAuditable { }
    public class EntityBase { }
    public class Order : EntityBase, IAuditable { public int Id { get; set; } public decimal Total { get; set; } public List<OrderLine> Lines { get; set; } public Customer Customer { get; set; } }
    public class PriorityOrder : Order { }
    public class OrderLine { public int Id { get; set; } }
    public class Customer { public int Id { get; set; } }
    public class Invoice { public int Id { get; set; } }
    public class OrderDto { public int Id { get; set; } }

    public class AppDbContext : DbContext
    {
        public DbSet<Order> Orders { get; set; }
        public DbSet<Invoice> Invoices { get; set; }
    }

    public static class QueryHelpers
    {
        public static IQueryable<TDestination> ProjectTo<TDestination>(this IQueryable source) => null;
        public static IQueryable<OrderDto> ToDtos(this IQueryable<Order> source) => null;
        public static IQueryable<int> SelectIds(this IQueryable<Order> source) => null;
        public static IQueryable<Order> Recent(this IQueryable<Order> source) => source;
        public static IQueryable<OrderLine> AllLines(this IQueryable<Order> source) => null;
        public static IQueryable<Customer> Buyers(this IQueryable<Order> source) => null;
        public static IQueryable<Invoice> InvoicesFor(this IQueryable<Order> source) => null;
        public static IQueryable<IGrouping<int, Order>> ByCustomer(this IQueryable<Order> source) => null;
    }
}
";

    private static string Program(string body) => Mock + @"
class Program
{
    public async Task<object> Run(AppDbContext db)
    {
        " + body + @"
    }
}
";

    [Theory]
    [InlineData("return db.Orders.ProjectTo<OrderDto>().ToList();")]
    [InlineData("return await db.Orders.ProjectTo<OrderDto>().ToListAsync();")]
    [InlineData("return db.Orders.Where(o => o.Total > 1).ToDtos().ToList();")]
    [InlineData("return db.Orders.SelectIds().ToList();")]
    [InlineData("return db.Set<Order>().ToDtos().FirstOrDefault();")]
    [InlineData("return db.Orders.Join(db.Invoices, o => o.Id, i => i.Id, (o, i) => new { OrderId = o.Id, InvoiceId = i.Id }).ToList();")]
    [InlineData("return await db.Orders.Join(db.Invoices, o => o.Id, i => i.Id, (o, i) => new { o.Id, o.Total }).ToListAsync();")]
    [InlineData("return db.Orders.GroupJoin(db.Invoices, o => o.Id, i => i.Id, (o, invoices) => new { o.Id, Count = invoices.Count() }).ToList();")]
    [InlineData("return db.Orders.Join(db.Invoices, o => o.Id, i => i.Id, (o, i) => new { o.Id, Totals = new { o.Total, Label = \"x\" } }).ToList();")]
    public Task HelperProjectingToNonEntity_NoDiagnostic(string body) =>
        VerifyCS.VerifyAnalyzerAsync(Program(body));

    [Theory]
    [InlineData("return {|LC009:db.Orders.Recent().ToList()|};")]
    [InlineData("return {|LC009:db.Orders.OfType<PriorityOrder>().ToList()|};")]
    [InlineData("return {|LC009:db.Orders.AllLines().ToList()|};")]
    [InlineData("return {|LC009:db.Orders.Buyers().ToList()|};")]
    [InlineData("return {|LC009:db.Orders.InvoicesFor().ToList()|};")]
    [InlineData("return {|LC009:db.Orders.SelectMany(o => o.Lines).ToList()|};")]
    [InlineData("return {|LC009:db.Orders.ByCustomer().ToList()|};")]
    [InlineData("return {|LC009:db.Orders.Cast<IAuditable>().ToList()|};")]
    [InlineData("return {|LC009:db.Orders.Cast<EntityBase>().ToList()|};")]
    [InlineData("return {|LC009:db.Orders.Join(db.Invoices, o => o.Id, i => i.Id, (o, i) => new { Order = o, i.Id }).ToList()|};")]
    [InlineData("return {|LC009:db.Orders.GroupJoin(db.Invoices, o => o.Id, i => i.Id, (o, invoices) => new { o.Id, Invoices = invoices }).ToList()|};")]
    [InlineData("return {|LC009:db.Orders.Join(db.Invoices, o => o.Id, i => i.Id, (o, i) => new { o.Id, Inner = new { Invoice = i } }).ToList()|};")]
    public Task HelperReturningEntities_StillReports(string body) =>
        VerifyCS.VerifyAnalyzerAsync(Program(body));
}
