﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Azure.Cosmos;
using Microsoft.Health.Fhir.Core.Features.Search;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Search.Queries
{
    internal interface IQueryBuilder
    {
        QueryDefinition BuildSqlQuerySpec(SearchOptions searchOptions, QueryBuilderOptions queryOptions = null);

        QueryDefinition GenerateHistorySql(SearchOptions searchOptions);

        QueryDefinition GenerateReindexSql(SearchOptions searchOptions, string searchParameterHash);
    }
}
