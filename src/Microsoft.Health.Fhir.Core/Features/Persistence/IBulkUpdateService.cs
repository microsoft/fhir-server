// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Operations.BulkUpdate;
using Microsoft.Health.Fhir.Core.Features.Resources.Patch;
using Microsoft.Health.Fhir.Core.Messages.Patch;
using Microsoft.Health.Fhir.Core.Messages.Upsert;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    public interface IBulkUpdateService
    {
        public Task<BulkUpdateResult> UpdateMultipleAsync(string resourceType, string fhirPatchParameters, bool readNextPage, uint readUpto, bool isIncludesRequest, IReadOnlyList<Tuple<string, string>> conditionalParameters, Models.BundleResourceContext bundleResourceContext, CancellationToken cancellationToken);
    }
}
