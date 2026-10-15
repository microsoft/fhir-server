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
    /// <summary>
    /// Provides operations for updating collections of resources.
    /// </summary>
    public interface IBulkUpdateService
    {
        /// <summary>
        /// Updates resources matching the supplied search and patch parameters.
        /// </summary>
        /// <param name="resourceType">The resource type to update.</param>
        /// <param name="fhirPatchParameters">The serialized FHIR Patch parameters.</param>
        /// <param name="readNextPage">Whether to process the next page of search results.</param>
        /// <param name="readUpto">The maximum number of additional pages to process.</param>
        /// <param name="isIncludesRequest">Whether the request processes included resources.</param>
        /// <param name="conditionalParameters">The conditional search parameters.</param>
        /// <param name="bundleResourceContext">The optional bundle resource context.</param>
        /// <param name="metaHistory">Whether metadata history should be retained.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <param name="allowProfileResourceModification">Whether protected profile resources may be modified.</param>
        /// <returns>The bulk update result.</returns>
        public Task<BulkUpdateResult> UpdateMultipleAsync(string resourceType, string fhirPatchParameters, bool readNextPage, uint readUpto, bool isIncludesRequest, IReadOnlyList<Tuple<string, string>> conditionalParameters, Models.BundleResourceContext bundleResourceContext, bool metaHistory, CancellationToken cancellationToken, bool allowProfileResourceModification = false);
    }
}
