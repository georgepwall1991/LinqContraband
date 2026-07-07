using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqContraband.Analyzers.LC037_RawSqlStringConstruction;

public sealed partial class RawSqlStringConstructionAnalyzer
{
    private static string? GetSimpleTypeName(TypeSyntax? type)
    {
        return type switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            QualifiedNameSyntax qualifiedName => GetSimpleTypeName(qualifiedName.Right),
            AliasQualifiedNameSyntax aliasQualifiedName => GetSimpleTypeName(aliasQualifiedName.Name),
            _ => null
        };
    }

    private static string? GetResolvedSimpleTypeName(TypeSyntax? type, SyntaxNode context)
    {
        var simpleName = GetSimpleTypeName(type);
        if (simpleName == null)
            return null;

        var root = context.SyntaxTree.GetRoot();
        foreach (var usingDirective in root.DescendantNodes().OfType<UsingDirectiveSyntax>())
        {
            if (usingDirective.Alias?.Name.Identifier.ValueText == simpleName)
                return GetSimpleTypeName(usingDirective.Name) ?? simpleName;
        }

        return simpleName;
    }

    private static bool HasLocalExceptionBase(
        string thrownType,
        string catchType,
        SyntaxNode context,
        int depth)
    {
        if (depth >= MaxLocalResolutionDepth)
            return false;

        var root = context.SyntaxTree.GetRoot();
        foreach (var classDeclaration in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            if (classDeclaration.Identifier.ValueText != thrownType ||
                classDeclaration.BaseList == null)
            {
                continue;
            }

            foreach (var baseType in classDeclaration.BaseList.Types)
            {
                var baseTypeName = GetResolvedSimpleTypeName(baseType.Type, classDeclaration);
                if (baseTypeName == null)
                    continue;

                if (baseTypeName == catchType ||
                    IsKnownExceptionBase(catchType, baseTypeName) ||
                    HasLocalExceptionBase(baseTypeName, catchType, context, depth + 1))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsKnownExceptionBase(string catchType, string thrownType)
    {
        return catchType switch
        {
            "SystemException" => thrownType is
                "AccessViolationException" or
                "ArgumentException" or
                "ArithmeticException" or
                "ArrayTypeMismatchException" or
                "BadImageFormatException" or
                "CannotUnloadAppDomainException" or
                "DivideByZeroException" or
                "DuplicateWaitObjectException" or
                "EndOfStreamException" or
                "ExecutionEngineException" or
                "FileLoadException" or
                "FileNotFoundException" or
                "FormatException" or
                "IndexOutOfRangeException" or
                "InsufficientMemoryException" or
                "InvalidCastException" or
                "InvalidOperationException" or
                "InvalidProgramException" or
                "IOException" or
                "MemberAccessException" or
                "NotFiniteNumberException" or
                "NotImplementedException" or
                "NullReferenceException" or
                "OperationCanceledException" or
                "OutOfMemoryException" or
                "OverflowException" or
                "RankException" or
                "StackOverflowException" or
                "SystemException" or
                "TimeoutException" or
                "TypeLoadException" or
                "UnauthorizedAccessException",
            "ArgumentException" => thrownType is
                "ArgumentNullException" or
                "ArgumentOutOfRangeException" or
                "DuplicateWaitObjectException",
            "ArithmeticException" => thrownType is
                "DivideByZeroException" or
                "NotFiniteNumberException" or
                "OverflowException",
            "IOException" => thrownType is
                "DirectoryNotFoundException" or
                "DriveNotFoundException" or
                "EndOfStreamException" or
                "FileLoadException" or
                "FileNotFoundException" or
                "PathTooLongException",
            "OperationCanceledException" => thrownType is "TaskCanceledException",
            _ => false
        };
    }
}
