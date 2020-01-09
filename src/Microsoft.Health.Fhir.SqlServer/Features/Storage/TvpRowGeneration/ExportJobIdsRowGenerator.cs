// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class ExportJobIdsRowGenerator : ITableValuedParameterRowGenerator<List<string>, V1.ExportJobIdsTableTypeRow>
    {
        public ExportJobIdsRowGenerator()
        {
        }

        public IEnumerable<V1.ExportJobIdsTableTypeRow> GenerateRows(List<string> exportJobsIds)
        {
            return exportJobsIds.Select(exportJobId => new V1.ExportJobIdsTableTypeRow(exportJobId));
        }
    }
}
