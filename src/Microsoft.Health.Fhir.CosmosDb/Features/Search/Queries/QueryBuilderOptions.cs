// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Search.Queries
{
    internal class QueryBuilderOptions
    {
        public QueryBuilderOptions(IReadOnlyList<IncludeExpression> includes = null, QueryProjection projection = QueryProjection.Default)
        {
            Includes = includes;
            Projection = projection;
        }

        /// <summary>
        /// References to project from the search index
        /// </summary>
        public IReadOnlyList<IncludeExpression> Includes { get; }

        /// <summary>
        /// The type of projected fields to return
        /// </summary>
        public QueryProjection Projection { get; }
    }
}
