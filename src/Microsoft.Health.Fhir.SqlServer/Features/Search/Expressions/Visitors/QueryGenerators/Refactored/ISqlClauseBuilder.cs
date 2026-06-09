// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.SqlServer;
using Microsoft.Health.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators.Refactored
{
    /// <summary>
    /// Interface for building specific SQL clauses.
    /// Allows each clause type to be implemented and tested independently.
    /// </summary>
    internal interface ISqlClauseBuilder
    {
        void Build(QueryGenerationContext context);
    }

    /// <summary>
    /// Builds WHERE clause for history filtering.
    /// </summary>
    internal interface IHistoryClauseBuilder
    {
        void AppendHistoryClause(
            IndentedStringBuilder.DelimitedScope delimited,
            ResourceVersionType resourceVersionTypes,
            QueryGenerationContext context,
            SearchParamTableExpression searchParamTableExpression = null,
            string tableAlias = null,
            Table tableName = null);
    }

    /// <summary>
    /// Builds WHERE clause for soft delete filtering.
    /// </summary>
    internal interface IDeletedClauseBuilder
    {
        void AppendDeletedClause(
            IndentedStringBuilder.DelimitedScope delimited,
            ResourceVersionType resourceVersionTypes,
            QueryGenerationContext context,
            string tableAlias = null);
    }
}
