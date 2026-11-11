using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace NPlusOneBenchmark.External;

/// <summary>A generic repository shipped from another project. Every method is one database round trip.</summary>
public class ExternalRepository<T>
    where T : class
{
    private readonly DbContext _db;

    public ExternalRepository(DbContext db) => _db = db;

    public T? FindById(int id) => _db.Set<T>().Find(id);

    public List<T> ListWhere(Expression<Func<T, bool>> predicate) => _db.Set<T>().Where(predicate).ToList();

    public int CountWhere(Expression<Func<T, bool>> predicate) => _db.Set<T>().Count(predicate);

    public IQueryable<T> Query() => _db.Set<T>();

    public void AddAndSave(T entity)
    {
        _db.Set<T>().Add(entity);
        _db.SaveChanges();
    }
}

/// <summary>Static data-access helpers from another project.</summary>
public static class ExternalQueries
{
    public static bool Exists<T>(DbContext db, Expression<Func<T, bool>> predicate)
        where T : class => db.Set<T>().Any(predicate);
}

/// <summary>An in-memory helper from another project: it never touches the database.</summary>
public static class ExternalFormatting
{
    public static string Label(int id, string name) => $"#{id} {name}";
}
