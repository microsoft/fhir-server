// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration.Merge;
using Microsoft.Health.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    /// <summary>
    /// Supplies an empty vector TVP until the semantic indexing layer owns vector extraction.
    /// </summary>
    internal sealed class VectorSearchParamListRowGenerator : ITableValuedParameterRowGenerator<IReadOnlyList<MergeResourceWrapper>, VectorSearchParamListRow>
    {
        public IEnumerable<VectorSearchParamListRow> GenerateRows(IReadOnlyList<MergeResourceWrapper> resources)
        {
            return Array.Empty<VectorSearchParamListRow>();
        }
    }
}
