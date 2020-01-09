// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class ExportJobIdsRowGenerator : ITableValuedParameterRowGenerator<List<ExportJobOutcome>, V1.ExportJobTableTypeRow>
    {
        public IEnumerable<V1.ExportJobTableTypeRow> GenerateRows(List<ExportJobOutcome> exportJobOutcomes)
        {
            return exportJobOutcomes.Select(exportJob => new V1.ExportJobTableTypeRow(exportJob.JobRecord.Id, RowVersionConverter.GetVersionAsBytes(exportJob.ETag.VersionId)));
        }
    }
}
