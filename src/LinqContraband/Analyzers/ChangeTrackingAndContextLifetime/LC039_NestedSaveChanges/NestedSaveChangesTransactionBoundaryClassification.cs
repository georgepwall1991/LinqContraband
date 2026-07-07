using System;
using System.Linq;
using LinqContraband.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace LinqContraband.Analyzers.LC039_NestedSaveChanges;

public sealed partial class NestedSaveChangesAnalyzer
{
    private sealed partial class AnalysisState
    {
        private static bool IsTransactionBoundaryInvocation(IInvocationOperation invocation)
        {
            if (!TransactionBoundaryMethodNames.Contains(invocation.TargetMethod.Name))
                return false;

            var receiverType = invocation.GetInvocationReceiverType();
            return IsEfCoreTransactionBoundaryType(receiverType);
        }

        private static bool IsEfCoreTransactionBoundaryType(ITypeSymbol? type)
        {
            if (type == null)
                return false;

            if (IsEfCoreTransactionBoundaryNamedType(type))
                return true;

            return type.AllInterfaces.Any(IsEfCoreTransactionBoundaryNamedType);
        }

        private static bool IsEfCoreTransactionBoundaryNamedType(ITypeSymbol type)
        {
            var namespaceName = type.ContainingNamespace?.ToString();
            if (namespaceName == null ||
                !namespaceName.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal))
            {
                return false;
            }

            return type.Name is "DatabaseFacade" or "IDbContextTransaction" or "DbContextTransaction";
        }
    }
}
