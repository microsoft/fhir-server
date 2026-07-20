// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Messages.Export;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Security
{
    /// <summary>
    /// Authorizes SMART scope and compartment restrictions for export operations.
    /// </summary>
    public interface IExportSmartScopeAuthorizer
    {
        /// <summary>
        /// Authorizes a SMART request to create an export job and determines the resource types to persist on it.
        /// </summary>
        /// <param name="request">The export request.</param>
        /// <returns>
        /// The canonical comma-separated resource types to persist on the export job, or <c>null</c> when the
        /// export is unconstrained.
        /// </returns>
        string AuthorizeCreateAndResolveResourceType(CreateExportRequest request);

        /// <summary>
        /// Authorizes access to an existing export job.
        /// </summary>
        /// <param name="exportJobRecord">The export job.</param>
        void AuthorizeJobAccess(ExportJobRecord exportJobRecord);
    }
}
