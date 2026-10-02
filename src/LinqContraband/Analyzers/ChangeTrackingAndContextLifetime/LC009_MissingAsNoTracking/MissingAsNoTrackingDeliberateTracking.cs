using System;
using System.Collections.Generic;
using System.Threading;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC009_MissingAsNoTracking;

/// <summary>
/// Tracked queries that are deliberate: a branch picked by an explicit tracking switch, a raw SQL write that
/// returns the changed rows, and an entity loaded to be attached to a newly created entity.
/// </summary>
public sealed partial class MissingAsNoTrackingAnalyzer
{
    // ---- raw SQL writes ----

    /// <summary>
    /// <c>FromSqlRaw("UPDATE ... RETURNING *")</c>, <c>FromSql($"DELETE ... OUTPUT DELETED.*")</c>: the constant
    /// SQL text starts, after whitespace and comments, with UPDATE, DELETE, INSERT or MERGE.
    /// </summary>
    private static bool IsRawSqlWrite(IInvocationOperation invocation)
    {
        if (invocation.TargetMethod.Name is not ("FromSqlRaw" or "FromSql" or "FromSqlInterpolated"))
            return false;

        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter?.Name != "sql")
                continue;

            var text = LeadingSqlText(argument.Value);
            return text != null && StartsWithWriteKeyword(text);
        }

        return false;
    }

    private static string? LeadingSqlText(IOperation value)
    {
        var unwrapped = value.UnwrapConversions();
        if (unwrapped.ConstantValue is { HasValue: true, Value: string constant })
            return constant;

        if (unwrapped is IInterpolatedStringOperation { Parts.Length: > 0 } interpolated &&
            interpolated.Parts[0] is IInterpolatedStringTextOperation { Text.ConstantValue: { HasValue: true, Value: string text } })
            return text;

        return null;
    }

    private static bool StartsWithWriteKeyword(string sql)
    {
        var index = 0;
        while (index < sql.Length)
        {
            if (char.IsWhiteSpace(sql[index]))
            {
                index++;
            }
            else if (string.CompareOrdinal(sql, index, "--", 0, 2) == 0)
            {
                var end = sql.IndexOf('\n', index);
                if (end < 0)
                    return false;
                index = end + 1;
            }
            else if (string.CompareOrdinal(sql, index, "/*", 0, 2) == 0)
            {
                var end = sql.IndexOf("*/", index + 2, StringComparison.Ordinal);
                if (end < 0)
                    return false;
                index = end + 2;
            }
            else
            {
                break;
            }
        }

        var start = index;
        while (index < sql.Length && char.IsLetter(sql[index]))
            index++;

        var keyword = sql.Substring(start, index - start);
        return keyword.Equals("UPDATE", StringComparison.OrdinalIgnoreCase) ||
               keyword.Equals("DELETE", StringComparison.OrdinalIgnoreCase) ||
               keyword.Equals("INSERT", StringComparison.OrdinalIgnoreCase) ||
               keyword.Equals("MERGE", StringComparison.OrdinalIgnoreCase);
    }

    // ---- explicit tracking switch ----

    /// <summary>
    /// True when the query sits in one branch of an <c>if</c>, ternary or <c>switch</c> whose condition reads a
    /// bool named like a tracking switch (<c>tracking</c>, <c>trackChanges</c>), or whose sibling branch runs a
    /// query over the same <c>DbSet</c> with <c>AsNoTracking()</c>. The code chooses tracking on purpose.
    /// </summary>
    private static bool IsDeliberatelyTrackedBranch(IInvocationOperation materializer, ITypeSymbol? dbSetType)
    {
        var entityType = DbSetEntityType(dbSetType);
        IOperation child = materializer;

        for (var parent = materializer.Parent; parent != null; child = parent, parent = parent.Parent)
        {
            if (parent is IMethodBodyBaseOperation or ILocalFunctionOperation or IAnonymousFunctionOperation)
                return false;

            switch (parent)
            {
                case IConditionalOperation conditional when child != conditional.Condition:
                {
                    if (ReadsTrackingFlag(conditional.Condition))
                        return true;

                    if (entityType == null)
                        break;

                    var sibling = child == conditional.WhenTrue ? conditional.WhenFalse : conditional.WhenTrue;
                    if (sibling != null && ContainsUntrackedQuery(sibling, entityType))
                        return true;

                    // if (forEdit) return db.X.First(...); return db.X.AsNoTracking().First(...);
                    if (sibling == null &&
                        EndsWithJump(conditional.WhenTrue) &&
                        conditional.Parent is IBlockOperation block &&
                        FollowingStatementsContainUntrackedQuery(block, conditional, entityType))
                        return true;

                    break;
                }

                case ISwitchCaseOperation section when section.Parent is ISwitchOperation switchStatement && entityType != null:
                    foreach (var otherSection in switchStatement.Cases)
                    {
                        if (otherSection == section)
                            continue;

                        foreach (var statement in otherSection.Body)
                        {
                            if (ContainsUntrackedQuery(statement, entityType))
                                return true;
                        }
                    }

                    break;

                case ISwitchExpressionArmOperation arm when arm.Parent is ISwitchExpressionOperation switchExpression &&
                                                            child == arm.Value && entityType != null:
                    foreach (var otherArm in switchExpression.Arms)
                    {
                        if (otherArm != arm && ContainsUntrackedQuery(otherArm.Value, entityType))
                            return true;
                    }

                    break;

                case IBlockOperation block when entityType != null:
                    // if (!forEdit) return db.X.AsNoTracking().First(...); return db.X.First(...);
                    foreach (var statement in block.Operations)
                    {
                        if (statement == child)
                            break;

                        if (statement is IConditionalOperation { WhenFalse: null } earlier &&
                            EndsWithJump(earlier.WhenTrue) &&
                            (ReadsTrackingFlag(earlier.Condition) || ContainsUntrackedQuery(earlier.WhenTrue, entityType)))
                            return true;
                    }

                    break;
            }
        }

        return false;
    }

    private static ITypeSymbol? DbSetEntityType(ITypeSymbol? dbSetType)
    {
        for (var type = dbSetType as INamedTypeSymbol; type != null; type = type.BaseType)
        {
            if (type.Name == "DbSet" && type.TypeArguments.Length == 1)
                return type.TypeArguments[0];
        }

        return null;
    }

    private static bool ReadsTrackingFlag(IOperation condition)
    {
        foreach (var operation in condition.DescendantsAndSelf())
        {
            if (operation.Type?.SpecialType != SpecialType.System_Boolean)
                continue;

            var name = operation switch
            {
                IParameterReferenceOperation parameter => parameter.Parameter.Name,
                ILocalReferenceOperation local => local.Local.Name,
                IFieldReferenceOperation field => field.Field.Name,
                IPropertyReferenceOperation property => property.Property.Name,
                _ => null
            };

            if (name != null && name.IndexOf("track", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        return false;
    }

    private static bool ContainsUntrackedQuery(IOperation operation, ITypeSymbol entityType)
    {
        foreach (var descendant in operation.DescendantsAndSelf())
        {
            if (descendant is IInvocationOperation
                {
                    TargetMethod.Name: "AsNoTracking" or "AsNoTrackingWithIdentityResolution"
                } untracked &&
                SymbolEqualityComparer.Default.Equals(DbSetEntityType(FindRootDbSetType(untracked)), entityType))
                return true;
        }

        return false;
    }

    private static ITypeSymbol? FindRootDbSetType(IInvocationOperation invocation)
    {
        var current = invocation.GetInvocationReceiver();
        while (current != null)
        {
            if (current.Type.IsDbSet())
                return current.Type;

            current = current is IInvocationOperation previous ? previous.GetInvocationReceiver() : null;
        }

        return null;
    }

    private static bool FollowingStatementsContainUntrackedQuery(IBlockOperation block, IOperation statement, ITypeSymbol entityType)
    {
        var seen = false;
        foreach (var operation in block.Operations)
        {
            if (seen && ContainsUntrackedQuery(operation, entityType))
                return true;

            if (operation == statement)
                seen = true;
        }

        return false;
    }

    private static bool EndsWithJump(IOperation? operation)
    {
        switch (operation)
        {
            case IReturnOperation:
            case IBranchOperation:
            case IExpressionStatementOperation { Operation: IThrowOperation }:
            case IThrowOperation:
                return true;
            case IBlockOperation { Operations.Length: > 0 } block:
                return EndsWithJump(block.Operations[block.Operations.Length - 1]);
            default:
                return false;
        }
    }

    // ---- attached to a new entity ----

    /// <summary>
    /// True when the loaded entity, or an element looked up from the loaded list
    /// (<c>categories.First(c => ...)</c>, <c>byAlias["books"]</c>), becomes a navigation of an object created in
    /// this body: <c>new Product { Category = category }</c>, <c>product.Category = category</c> or
    /// <c>product.Categories.Add(category)</c> on a local initialized with <c>new Product()</c>, or a collection
    /// initializer of such a navigation. Saving the new object with an untracked related entity inserts that entity
    /// again, so the query needs tracking.
    /// </summary>
    private static bool MaterializedEntityIsAttachedToNewEntity(
        IInvocationOperation materializer,
        IOperation root,
        HashSet<ILocalSymbol> entityLocals,
        CancellationToken cancellationToken)
    {
        if (IsAttachedToNewEntity(materializer, root))
            return true;

        if (entityLocals.Count == 0)
            return false;

        foreach (var descendant in root.Descendants())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (descendant is ILocalReferenceOperation localReference &&
                entityLocals.Contains(localReference.Local) &&
                IsAttachedToNewEntity(localReference, root))
                return true;
        }

        return false;
    }

    private static bool IsAttachedToNewEntity(IOperation entity, IOperation root)
    {
        // Climb through lookups that still yield the loaded entity or entities.
        var current = entity;
        while (true)
        {
            var parent = current.Parent;
            if (parent is IConversionOperation or IParenthesizedOperation or IAwaitOperation ||
                parent is ICoalesceOperation coalesce && coalesce.Value == current ||
                parent is IPropertyReferenceOperation { Property.IsIndexer: true } indexer && indexer.Instance == current)
            {
                current = parent;
                continue;
            }

            if (parent is IArgumentOperation { Parent: IInvocationOperation linq } argument &&
                IsLinqToObjectsSource(linq, argument) &&
                ReachesEntities(linq.Type))
            {
                current = linq;
                continue;
            }

            break;
        }

        switch (current.Parent)
        {
            // new Product { Category = category } or product.Category = category
            case ISimpleAssignmentOperation assignment when assignment.Value == current:
                return assignment.Target is IPropertyReferenceOperation { Property.IsIndexer: false } navigation &&
                       ReachesEntities(navigation.Type) &&
                       IsNewEntity(navigation.Instance, root);

            // product.Categories.Add(category), or new Product { Categories = { category } }
            case IArgumentOperation { Parent: IInvocationOperation { TargetMethod.Name: "Add" } add }:
                var collection = add.Instance?.UnwrapConversions();
                if (collection is IInstanceReferenceOperation &&
                    add.Parent is IObjectOrCollectionInitializerOperation { Parent: IMemberInitializerOperation memberInitializer })
                    collection = memberInitializer.InitializedMember;

                return collection is IPropertyReferenceOperation { Property.IsIndexer: false } navigationCollection &&
                       ReachesEntities(navigationCollection.Type) &&
                       IsNewEntity(navigationCollection.Instance, root);

            default:
                return false;
        }
    }

    // The receiver is an object created in this body: the implicit receiver of an object initializer, or a local
    // declared with a new expression. The created type must be application code, not a System or anonymous type.
    private static bool IsNewEntity(IOperation? instance, IOperation root)
    {
        switch (instance?.UnwrapConversions())
        {
            case IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ImplicitReceiver } implicitReceiver:
                for (var ancestor = implicitReceiver.Parent; ancestor != null; ancestor = ancestor.Parent)
                {
                    if (ancestor is IObjectOrCollectionInitializerOperation initializer)
                        return initializer.Parent is IObjectCreationOperation creation && IsApplicationType(creation.Type);
                }

                return false;

            case ILocalReferenceOperation local:
                foreach (var descendant in root.Descendants())
                {
                    if (descendant is IVariableDeclaratorOperation declarator &&
                        SymbolEqualityComparer.Default.Equals(declarator.Symbol, local.Local))
                    {
                        var initializer = declarator.Initializer?.Value ?? declarator.GetVariableInitializer()?.Value;
                        return initializer?.UnwrapConversions() is IObjectCreationOperation creation &&
                               IsApplicationType(creation.Type);
                    }
                }

                return false;

            default:
                return false;
        }
    }

    private static bool IsApplicationType(ITypeSymbol? type) =>
        type is INamedTypeSymbol { TypeKind: TypeKind.Class, IsAnonymousType: false } named && !IsInSystemNamespace(named);
}
