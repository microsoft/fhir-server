// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators.Refactored.ClauseBuilders
{
    /// <summary>
    /// Builds WHERE clauses for filtering soft-deleted resources.
    /// </summary>
    internal class DeletedClauseBuilder : IDeletedClauseBuilder
    {
        public void AppendDeletedClause(
            IndentedStringBuilder.DelimitedScope delimited,
            ResourceVersionType resourceVersionTypes,
            QueryGenerationContext context,
            string tableAlias = null)
        {
            if (resourceVersionTypes.HasFlag(ResourceVersionType.SoftDeleted))
            {
                return;
            }

            delimited.BeginDelimitedElement();
            context.StringBuilder.Append(VLatest.Resource.IsDeleted, tableAlias).Append(" = 0");
        }
    }
}
