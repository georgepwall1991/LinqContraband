using System;

namespace NPlusOneBenchmark.Corpus;

/// <summary>What kind of round trip the case repeats (or, for a safe case, resembles).</summary>
public enum Kind
{
    /// <summary>A query, <c>Find</c> or explicit load per element.</summary>
    Query,

    /// <summary>A navigation read that lazy-loads per element.</summary>
    LazyLoad,

    /// <summary>A save or bulk command per element.</summary>
    Save,
}

/// <summary>How the database call is reached from the iteration.</summary>
public enum Via
{
    /// <summary>Written directly in a loop statement.</summary>
    Direct,

    /// <summary>Inside a lambda that an in-memory operator runs per element.</summary>
    Lambda,

    /// <summary>Through a non-overridable helper whose source is in the same project.</summary>
    Helper,

    /// <summary>Through a helper or parameter that hands over an <c>IQueryable</c> or <c>DbSet</c>.</summary>
    QueryableHelper,

    /// <summary>Through an interface.</summary>
    Interface,

    /// <summary>Through a virtual or abstract method.</summary>
    Virtual,

    /// <summary>Through a delegate.</summary>
    Delegate,

    /// <summary>Through a mediator-style dispatcher.</summary>
    Mediator,

    /// <summary>The entities come from, or go to, another method.</summary>
    CrossMethod,

    /// <summary>Through a library in another project, visible only as metadata.</summary>
    CrossProject,
}

/// <summary>A case that must make more database round trips as the data grows.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class NPlusOneAttribute : Attribute
{
    public NPlusOneAttribute(Kind kind, Via via)
    {
        Kind = kind;
        Via = via;
    }

    public Kind Kind { get; }

    public Via Via { get; }
}

/// <summary>A case that must make the same number of database round trips whatever the data size.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class SafeAttribute : Attribute
{
    public SafeAttribute(Kind kind, Via via)
    {
        Kind = kind;
        Via = via;
    }

    public Kind Kind { get; }

    public Via Via { get; }
}
