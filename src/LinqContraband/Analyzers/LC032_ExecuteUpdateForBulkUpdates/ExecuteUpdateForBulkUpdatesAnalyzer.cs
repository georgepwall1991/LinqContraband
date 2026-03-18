using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC032_ExecuteUpdateForBulkUpdates;

/// <summary>
/// Detects tracked bulk-update loops that can likely be replaced with ExecuteUpdate/ExecuteUpdateAsync. Diagnostic ID: LC032
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ExecuteUpdateForBulkUpdatesAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "LC032";
    private const string Category = "Performance";
    private static readonly LocalizableString Title = "Use ExecuteUpdate for provable bulk scalar updates";

    private static readonly LocalizableString MessageFormat =
        "Loop updates tracked '{0}' entities and then calls '{1}'. Consider ExecuteUpdate/ExecuteUpdateAsync for a set-based update. Warning: ExecuteUpdate bypasses change tracking and entity callbacks.";

    private static readonly LocalizableString Description =
        "Reports only when a foreach loop over a provable EF query performs direct scalar assignments on tracked entities and is immediately followed by SaveChanges/SaveChangesAsync on the same local DbContext.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Info,
        true,
        Description,
        helpLinkUri: "https://github.com/georgewall/LinqContraband/blob/main/docs/LC032_ExecuteUpdateForBulkUpdates.md");

    private static readonly ImmutableHashSet<string> AllowedQuerySteps = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "Where",
        "OrderBy",
        "OrderByDescending",
        "ThenBy",
        "ThenByDescending",
        "Skip",
        "Take",
        "Distinct",
        "Include",
        "ThenInclude",
        "IgnoreQueryFilters",
        "AsSplitQuery",
        "AsSingleQuery",
        "AsTracking",
        "TagWith",
        "TagWithCallSite"
    );

    private static readonly ImmutableHashSet<string> MaterializerSteps = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "ToList",
        "ToListAsync",
        "ToArray",
        "ToArrayAsync"
    );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(InitializeCompilation);
    }

    private static void InitializeCompilation(CompilationStartAnalysisContext context)
    {
        if (!HasExecuteUpdateSupport(context.Compilation))
            return;

        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;

        if (method.Name is not ("SaveChanges" or "SaveChangesAsync"))
            return;

        if (!method.ContainingType.IsDbContext())
            return;

        if (invocation.Instance?.UnwrapConversions() is not ILocalReferenceOperation dbContextReference)
            return;

        if (!TryGetImmediatelyPreviousForEachLoop(invocation, out var loop))
            return;

        if (!invocation.SharesOwningExecutableRoot(loop))
            return;

        if (!TryAnalyzeLoop(loop, dbContextReference.Local, out var entityTypeName))
            return;

        context.ReportDiagnostic(
            Diagnostic.Create(
                Rule,
                loop.Syntax.GetLocation(),
                entityTypeName,
                method.Name));
    }

    private static bool TryAnalyzeLoop(
        IForEachLoopOperation loop,
        ILocalSymbol dbContextLocal,
        out string entityTypeName)
    {
        entityTypeName = "entity";

        if (loop.IsAsynchronous || loop.Locals.Length != 1)
            return false;

        var iterationLocal = loop.Locals[0];
        entityTypeName = iterationLocal.Type.Name;

        if (!TryResolveTrackedQuerySource(loop.Collection, loop, dbContextLocal))
            return false;

        return HasOnlyDirectScalarAssignments(loop.Body, iterationLocal);
    }

    private static bool TryResolveTrackedQuerySource(
        IOperation collection,
        IForEachLoopOperation loop,
        ILocalSymbol dbContextLocal)
    {
        var current = collection.UnwrapConversions();
        var visitedLocals = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);

        while (current != null)
        {
            current = current.UnwrapConversions();

            switch (current)
            {
                case IInvocationOperation invocation:
                    if (IsDbContextSetInvocation(invocation, dbContextLocal))
                        return true;

                    if (MaterializerSteps.Contains(invocation.TargetMethod.Name))
                    {
                        current = invocation.GetInvocationReceiver();
                        continue;
                    }

                    if (AllowedQuerySteps.Contains(invocation.TargetMethod.Name))
                    {
                        current = invocation.GetInvocationReceiver();
                        continue;
                    }

                    return false;

                case IPropertyReferenceOperation propertyReference:
                    return IsMatchingDbSetProperty(propertyReference, dbContextLocal);

                case ILocalReferenceOperation localReference:
                    if (!visitedLocals.Add(localReference.Local))
                        return false;

                    if (localReference.Type.IsIQueryable())
                    {
                        if (!TryGetSingleAssignedLocalValue(localReference.Local, loop, out var queryValue))
                            return false;

                        current = queryValue;
                        continue;
                    }

                    if (!TryGetImmediatePreviousLocalValue(loop, localReference.Local, out var valueOperation))
                        return false;

                    if (!TryGetSingleAssignedLocalValue(localReference.Local, loop, out _))
                        return false;

                    current = valueOperation;
                    continue;

                default:
                    return false;
            }
        }

        return false;
    }

    private static bool HasOnlyDirectScalarAssignments(IOperation body, ILocalSymbol iterationLocal)
    {
        var statements = body is IBlockOperation block
            ? block.Operations
            : ImmutableArray.Create(body);

        if (statements.Length == 0)
            return false;

        foreach (var statement in statements)
        {
            if (statement is not IExpressionStatementOperation expressionStatement)
                return false;

            if (expressionStatement.Operation.UnwrapConversions() is not ISimpleAssignmentOperation assignment)
                return false;

            if (!IsDirectScalarAssignment(assignment, iterationLocal))
                return false;
        }

        return true;
    }

    private static bool IsDirectScalarAssignment(ISimpleAssignmentOperation assignment, ILocalSymbol iterationLocal)
    {
        var target = assignment.Target.UnwrapConversions();
        if (target is not IPropertyReferenceOperation propertyReference)
            return false;

        if (propertyReference.Instance?.UnwrapConversions() is not ILocalReferenceOperation localReference ||
            !SymbolEqualityComparer.Default.Equals(localReference.Local, iterationLocal))
        {
            return false;
        }

        if (!IsScalarLikeType(propertyReference.Property.Type))
            return false;

        return IsSafeScalarValueExpression(assignment.Value, iterationLocal);
    }

    private static bool IsSafeScalarValueExpression(IOperation operation, ILocalSymbol iterationLocal)
    {
        var current = operation.UnwrapConversions();
        if (current.ConstantValue.HasValue)
            return true;

        return current switch
        {
            IDefaultValueOperation => true,
            ILocalReferenceOperation => true,
            IParameterReferenceOperation => true,
            IPropertyReferenceOperation propertyReference => IsSafeMemberReference(propertyReference.Instance, propertyReference.Type, iterationLocal),
            IFieldReferenceOperation fieldReference => IsSafeFieldReference(fieldReference, iterationLocal),
            IBinaryOperation binaryOperation =>
                IsSafeScalarValueExpression(binaryOperation.LeftOperand, iterationLocal) &&
                IsSafeScalarValueExpression(binaryOperation.RightOperand, iterationLocal),
            IUnaryOperation unaryOperation => IsSafeScalarValueExpression(unaryOperation.Operand, iterationLocal),
            _ => false
        };
    }

    private static bool IsSafeMemberReference(IOperation? instance, ITypeSymbol? memberType, ILocalSymbol iterationLocal)
    {
        if (!IsScalarLikeType(memberType))
            return false;

        if (instance == null)
            return false;

        var unwrappedInstance = instance.UnwrapConversions();
        return unwrappedInstance is ILocalReferenceOperation localReference &&
               SymbolEqualityComparer.Default.Equals(localReference.Local, iterationLocal);
    }

    private static bool IsSafeFieldReference(IFieldReferenceOperation fieldReference, ILocalSymbol iterationLocal)
    {
        if (!IsScalarLikeType(fieldReference.Type))
            return false;

        if (fieldReference.Instance == null)
            return fieldReference.Field.ContainingType.TypeKind == TypeKind.Enum;

        return fieldReference.Instance.UnwrapConversions() is ILocalReferenceOperation localReference &&
               SymbolEqualityComparer.Default.Equals(localReference.Local, iterationLocal);
    }

    private static bool IsScalarLikeType(ITypeSymbol? type)
    {
        if (type == null)
            return false;

        if (type is INamedTypeSymbol namedType &&
            namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
            namedType.TypeArguments.Length == 1)
        {
            type = namedType.TypeArguments[0];
        }

        if (type.TypeKind == TypeKind.Enum)
            return true;

        if (type.SpecialType is
            SpecialType.System_Boolean or
            SpecialType.System_Byte or
            SpecialType.System_Char or
            SpecialType.System_Decimal or
            SpecialType.System_Double or
            SpecialType.System_Int16 or
            SpecialType.System_Int32 or
            SpecialType.System_Int64 or
            SpecialType.System_SByte or
            SpecialType.System_Single or
            SpecialType.System_String or
            SpecialType.System_UInt16 or
            SpecialType.System_UInt32 or
            SpecialType.System_UInt64)
        {
            return true;
        }

        var displayName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return displayName is
            "global::System.DateTime" or
            "global::System.DateTimeOffset" or
            "global::System.Guid" or
            "global::System.TimeSpan";
    }

    private static bool TryGetImmediatelyPreviousForEachLoop(
        IInvocationOperation invocation,
        out IForEachLoopOperation loop)
    {
        loop = null!;

        if (!TryGetImmediatelyPreviousStatement(invocation, out var previousStatement))
            return false;

        if (previousStatement is not IForEachLoopOperation forEachLoop)
            return false;

        loop = forEachLoop;
        return true;
    }

    private static bool TryGetImmediatePreviousLocalValue(
        IForEachLoopOperation loop,
        ILocalSymbol local,
        out IOperation valueOperation)
    {
        valueOperation = null!;

        if (!TryGetImmediatelyPreviousStatement(loop, out var previousStatement))
            return false;

        if (previousStatement is IVariableDeclarationGroupOperation declarationGroup)
        {
            foreach (var declaration in declarationGroup.Declarations)
            {
                foreach (var declarator in declaration.Declarators)
                {
                    if (SymbolEqualityComparer.Default.Equals(declarator.Symbol, local) &&
                        declarator.Initializer != null)
                    {
                        valueOperation = declarator.Initializer.Value;
                        return true;
                    }
                }
            }
        }

        if (previousStatement is IExpressionStatementOperation expressionStatement &&
            expressionStatement.Operation.UnwrapConversions() is ISimpleAssignmentOperation assignment &&
            assignment.Target.UnwrapConversions() is ILocalReferenceOperation localReference &&
            SymbolEqualityComparer.Default.Equals(localReference.Local, local))
        {
            valueOperation = assignment.Value;
            return true;
        }

        return false;
    }

    private static bool TryGetImmediatelyPreviousStatement(IOperation operation, out IOperation previousStatement)
    {
        previousStatement = null!;
        var currentStatement = FindContainingStatement(operation);

        if (currentStatement?.Parent is not IBlockOperation block)
            return false;

        for (var i = 0; i < block.Operations.Length; i++)
        {
            if (!ReferenceEquals(block.Operations[i], currentStatement))
                continue;

            if (i == 0)
                return false;

            previousStatement = block.Operations[i - 1];
            return true;
        }

        return false;
    }

    private static IOperation? FindContainingStatement(IOperation operation)
    {
        var current = operation;
        while (current != null)
        {
            if (current.Parent is IBlockOperation)
                return current;

            current = current.Parent;
        }

        return null;
    }

    private static bool IsMatchingDbSetProperty(IPropertyReferenceOperation propertyReference, ILocalSymbol dbContextLocal)
    {
        return propertyReference.Type.IsDbSet() &&
               propertyReference.Instance?.UnwrapConversions() is ILocalReferenceOperation localReference &&
               SymbolEqualityComparer.Default.Equals(localReference.Local, dbContextLocal);
    }

    private static bool IsDbContextSetInvocation(IInvocationOperation invocation, ILocalSymbol dbContextLocal)
    {
        return invocation.TargetMethod.Name == "Set" &&
               invocation.TargetMethod.ContainingType.IsDbContext() &&
               invocation.Instance?.UnwrapConversions() is ILocalReferenceOperation localReference &&
               SymbolEqualityComparer.Default.Equals(localReference.Local, dbContextLocal);
    }

    private static bool TryGetSingleAssignedLocalValue(
        ILocalSymbol local,
        IOperation analysisScope,
        out IOperation valueOperation)
    {
        valueOperation = null!;

        if (local.DeclaringSyntaxReferences.Length != 1)
            return false;

        var declarator = local.DeclaringSyntaxReferences[0].GetSyntax() as VariableDeclaratorSyntax;
        if (declarator?.Initializer?.Value == null)
            return false;

        var semanticModel = analysisScope.SemanticModel;
        var executableRoot = analysisScope.FindOwningExecutableRoot();
        if (semanticModel == null || executableRoot == null)
            return false;

        if (HasLocalWrites(local, executableRoot.Syntax, semanticModel))
            return false;

        var operation = semanticModel.GetOperation(declarator.Initializer.Value);
        if (operation == null)
            return false;

        valueOperation = operation;
        return true;
    }

    private static bool HasLocalWrites(ILocalSymbol local, SyntaxNode executableRootSyntax, SemanticModel semanticModel)
    {
        foreach (var assignment in executableRootSyntax.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            if (SymbolEqualityComparer.Default.Equals(semanticModel.GetSymbolInfo(assignment.Left).Symbol, local))
                return true;
        }

        foreach (var prefix in executableRootSyntax.DescendantNodes().OfType<PrefixUnaryExpressionSyntax>())
        {
            if (!prefix.IsKind(SyntaxKind.PreIncrementExpression) &&
                !prefix.IsKind(SyntaxKind.PreDecrementExpression))
            {
                continue;
            }

            if (SymbolEqualityComparer.Default.Equals(semanticModel.GetSymbolInfo(prefix.Operand).Symbol, local))
                return true;
        }

        foreach (var postfix in executableRootSyntax.DescendantNodes().OfType<PostfixUnaryExpressionSyntax>())
        {
            if (!postfix.IsKind(SyntaxKind.PostIncrementExpression) &&
                !postfix.IsKind(SyntaxKind.PostDecrementExpression))
            {
                continue;
            }

            if (SymbolEqualityComparer.Default.Equals(semanticModel.GetSymbolInfo(postfix.Operand).Symbol, local))
                return true;
        }

        return false;
    }

    private static bool HasExecuteUpdateSupport(Compilation compilation)
    {
        var extensionsType = compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions");
        if (extensionsType != null &&
            (extensionsType.GetMembers("ExecuteUpdate").OfType<IMethodSymbol>().Any() ||
             extensionsType.GetMembers("ExecuteUpdateAsync").OfType<IMethodSymbol>().Any()))
        {
            return true;
        }

        return compilation.GetSymbolsWithName("ExecuteUpdate", SymbolFilter.Member)
                   .OfType<IMethodSymbol>()
                   .Any(IsExecuteUpdateLikeMethod) ||
               compilation.GetSymbolsWithName("ExecuteUpdateAsync", SymbolFilter.Member)
                   .OfType<IMethodSymbol>()
                   .Any(IsExecuteUpdateLikeMethod);
    }

    private static bool IsExecuteUpdateLikeMethod(IMethodSymbol method)
    {
        if (!method.IsExtensionMethod || method.Parameters.Length == 0)
            return false;

        return method.Parameters[0].Type.IsIQueryable();
    }
}
