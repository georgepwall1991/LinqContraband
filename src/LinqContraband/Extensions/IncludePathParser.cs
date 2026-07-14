using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Extensions;

/// <summary>
/// A single navigation step inside an Include/ThenInclude path, e.g. the "Items" in
/// <c>Include(o => o.Items)</c>.
/// </summary>
internal readonly struct NavigationSegment
{
    public NavigationSegment(string name, bool isCollection)
    {
        Name = name;
        IsCollection = isCollection;
    }

    public string Name { get; }
    public bool IsCollection { get; }
}

/// <summary>
/// A full eager-loading path built from one Include plus any chained ThenIncludes,
/// e.g. "Orders.Items" for <c>Include(c => c.Orders).ThenInclude(o => o.Items)</c>.
/// </summary>
internal sealed class IncludePath
{
    public IncludePath(ImmutableArray<NavigationSegment> segments)
    {
        Segments = segments;
    }

    public ImmutableArray<NavigationSegment> Segments { get; }

    public string Key => string.Join(".", Segments.Select(segment => segment.Name));

    public IncludePath Append(IncludePath childPath)
    {
        return new IncludePath(Segments.AddRange(childPath.Segments));
    }
}

/// <summary>
/// Parses EF Core Include/ThenInclude invocations (lambda, filtered-lambda, and constant-string
/// overloads) into <see cref="IncludePath"/> values. Shared by LC006 (cartesian explosion) and
/// LC045 (missing include).
/// </summary>
internal static partial class IncludePathParser
{
    public static bool TryGetIncludePath(
        IInvocationOperation invocation,
        SemanticModel semanticModel,
        IncludePath? previousIncludePath,
        out IncludePath includePath
    )
    {
        includePath = new IncludePath(ImmutableArray<NavigationSegment>.Empty);

        if (
            invocation.TargetMethod.Name == "Include"
            && TryGetStringIncludePath(invocation, out includePath)
        )
        {
            return true;
        }

        if (!TryGetLambdaExpression(invocation, out var lambdaExpression))
            return false;

        if (!TryGetNavigationPath(lambdaExpression.Body, semanticModel, out var lambdaPath))
            return false;

        if (invocation.TargetMethod.Name == "ThenInclude")
        {
            if (previousIncludePath == null)
                return false;

            includePath = previousIncludePath.Append(lambdaPath);
            return true;
        }

        includePath = lambdaPath;
        return true;
    }

    private static bool TryGetStringIncludePath(
        IInvocationOperation invocation,
        out IncludePath includePath
    )
    {
        includePath = new IncludePath(ImmutableArray<NavigationSegment>.Empty);
        var navigationArgument = invocation.Arguments.FirstOrDefault(argument =>
            argument.Parameter?.Ordinal == GetNavigationParameterOrdinal(invocation)
        );
        if (navigationArgument == null)
            return false;

        var value = navigationArgument.Value;
        if (!value.ConstantValue.HasValue || value.ConstantValue.Value is not string pathText)
            return false;

        if (string.IsNullOrWhiteSpace(pathText))
            return false;

        var receiverType = GetSourceType(invocation);
        if (!TryGetQueryableElementType(receiverType, out var currentType))
            return false;

        var builder = ImmutableArray.CreateBuilder<NavigationSegment>();
        foreach (var rawSegment in pathText.Split('.'))
        {
            var segmentName = rawSegment.Trim();
            if (segmentName.Length == 0)
                return false;

            var property = TryFindProperty(currentType, segmentName);
            if (property == null)
                return false;

            var propertyType = property.Type;
            var isCollection = IsCollection(propertyType);
            builder.Add(new NavigationSegment(property.Name, isCollection));

            if (isCollection)
            {
                if (!TryGetCollectionElementType(propertyType, out currentType))
                    return false;
            }
            else
            {
                currentType = propertyType;
            }
        }

        includePath = new IncludePath(builder.ToImmutable());
        return true;
    }

    private static ITypeSymbol? GetSourceType(IInvocationOperation invocation)
    {
        if (invocation.Instance != null)
            return invocation.Instance.Type;

        return invocation
            .Arguments.FirstOrDefault(argument => argument.Parameter?.Ordinal == 0)
            ?.Value.Type;
    }

    private static int GetNavigationParameterOrdinal(IInvocationOperation invocation)
    {
        return invocation.Instance == null ? 1 : 0;
    }
}
