// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators
{
    /// <summary>
    /// Generates predicates for <see cref="SqlSearchParameters.PrimaryKeyParameter"/>.
    /// These take the from of
    /// ResourceTypeId = currentTypeId AND ResourceSurrogateId > currentSurrogateId OR ResourceTypeId IN (subsequentIDs)
    /// </summary>
    internal class PrimaryKeyRangeParameterQueryGenerator : ResourceTableSearchParameterQueryGenerator
    {
        public static new readonly PrimaryKeyRangeParameterQueryGenerator Instance = new();

        public override SearchParameterQueryGeneratorContext VisitBinary(BinaryExpression expression, SearchParameterQueryGeneratorContext context)
        {
            var primaryKeyRange = (PrimaryKeyRange)expression.Value;

            VisitSimpleBinary(BinaryOperator.Equal, context, VLatest.Resource.ResourceTypeId, null, primaryKeyRange.CurrentValue.ResourceTypeId, includeInParameterHash: false);
            context.StringBuilder.Append(" AND ");
            VisitSimpleBinary(expression.BinaryOperator, context, VLatest.Resource.ResourceSurrogateId, null, primaryKeyRange.CurrentValue.ResourceSurrogateId, includeInParameterHash: false);

            return context;
        }
    }
}
