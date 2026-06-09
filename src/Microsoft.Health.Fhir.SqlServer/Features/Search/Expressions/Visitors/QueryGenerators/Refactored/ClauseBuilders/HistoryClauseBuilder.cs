// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer;
using Microsoft.Health.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators.Refactored.ClauseBuilders
{
    /// <summary>
    /// Builds WHERE clauses for filtering by resource version history.
    /// </summary>
    internal class HistoryClauseBuilder : IHistoryClauseBuilder
    {
        public void AppendHistoryClause(
            IndentedStringBuilder.DelimitedScope delimited,
            ResourceVersionType resourceVersionTypes,
            QueryGenerationContext context,
            SearchParamTableExpression searchParamTableExpression = null,
            string tableAlias = null,
            Table tableName = null)
        {
            if (resourceVersionTypes.HasFlag(ResourceVersionType.History))
            {
                return;
            }

            delimited.BeginDelimitedElement();

            var effectiveTableName = tableName ?? VLatest.Resource;
            var isLatestColumn = VLatest.Resource.IsHistory;

            context.StringBuilder.Append(isLatestColumn, tableAlias).Append(" = 0");
        }
    }
}
