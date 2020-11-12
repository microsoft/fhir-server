// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Azure.Cosmos;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Search.Queries
{
    public interface IQueryBuilder
    {
        QueryDefinition BuildSqlQuerySpec(SearchOptions searchOptions, IReadOnlyList<IncludeExpression> includes);

        QueryDefinition GenerateHistorySql(SearchOptions searchOptions);

        QueryDefinition GenerateReindexSql(SearchOptions searchOptions, string searchParameterHash);
    }
}
