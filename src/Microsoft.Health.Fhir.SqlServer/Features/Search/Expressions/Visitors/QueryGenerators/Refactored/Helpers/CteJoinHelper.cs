// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators.Refactored.Helpers
{
    /// <summary>
    /// Helper class for managing CTE joins and intersections.
    /// Extracted to reduce complexity in individual generators.
    /// </summary>
    internal static class CteJoinHelper
    {
        public static int FindRestrictingPredecessorIndex(QueryGenerationContext context)
        {
            return context.CurrentCteIndex - 1;
        }

        public static bool ShouldUseInnerJoin(QueryGenerationContext context)
        {
            // Logic to determine if we should use INNER JOIN vs WHERE EXISTS
            // Based on previous SqlQueryGenerator.UseAppendWithJoin() logic
            return context.SearchParamCount > 1 || context.HasIdentifier;
        }

        public static void AppendIntersectionWithPredecessorUsingInnerJoin(
            QueryGenerationContext context,
            SearchParamTableExpression expression)
        {
            int predecessorIndex = FindRestrictingPredecessorIndex(context);
            if (predecessorIndex < 0)
            {
                return;
            }

            var sb = context.StringBuilder;
            var cteName = QueryGenerationContext.GetCteTableName(predecessorIndex);

            sb.Append("     JOIN ").Append(cteName)
                .Append(" ON ").Append(VLatest.Resource.ResourceTypeId, null).Append(" = ").Append(cteName).Append(".T1")
                .Append(" AND ").Append(VLatest.Resource.ResourceSurrogateId, null).Append(" = ").Append(cteName).AppendLine(".Sid1");
        }

        public static void AppendIntersectionWithPredecessor(
            IndentedStringBuilder.DelimitedScope delimited,
            QueryGenerationContext context,
            SearchParamTableExpression expression)
        {
            int predecessorIndex = FindRestrictingPredecessorIndex(context);
            if (predecessorIndex < 0)
            {
                return;
            }

            delimited.BeginDelimitedElement();
            var cteName = QueryGenerationContext.GetCteTableName(predecessorIndex);

            context.StringBuilder
                .Append("EXISTS (SELECT * FROM ").Append(cteName)
                .Append(" WHERE ").Append(cteName).Append(".T1 = ").Append(VLatest.Resource.ResourceTypeId, null)
                .Append(" AND ").Append(cteName).Append(".Sid1 = ").Append(VLatest.Resource.ResourceSurrogateId, null)
                .Append(")");
        }
    }
}
