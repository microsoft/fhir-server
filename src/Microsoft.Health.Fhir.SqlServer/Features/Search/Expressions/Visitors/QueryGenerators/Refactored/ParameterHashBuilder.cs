// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators.Refactored
{
    /// <summary>
    /// Helper for building parameter hash comments.
    /// </summary>
    internal class ParameterHashBuilder
    {
        public static void AddParametersHash(QueryGenerationContext context, bool forSmartV2Include = false)
        {
            foreach (var searchParamId in context.Parameters.SearchParamIds)
            {
                context.SearchParamIds.Add(searchParamId);
            }

            if (!context.Parameters.HasParametersToHash || context.ReuseQueryPlans)
            {
                context.StringBuilder.AppendLine();
                return;
            }

            var sb = context.StringBuilder;
            sb.Append(SqlQueryGenerator.ParametersHashStart);

            if (forSmartV2Include)
            {
                context.Parameters.AppendSmartScopeHash(sb);
                context.Parameters.AppendSmartScopeParameterNames(sb);
            }
            else
            {
                context.Parameters.AppendHash(sb);
                context.Parameters.AppendHashedParameterNames(sb);
            }

            sb.Append(SqlQueryGenerator.ParametersHashEnd);
            sb.AppendLine();
        }
    }
}
