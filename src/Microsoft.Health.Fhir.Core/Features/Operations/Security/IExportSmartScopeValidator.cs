// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Messages.Export;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Security
{
    /// <summary>
    /// Validates SMART scope and compartment restrictions for export operations.
    /// </summary>
    public interface IExportSmartScopeValidator
    {
        /// <summary>
        /// Validates a request to create an export job.
        /// </summary>
        /// <param name="request">The export request.</param>
        /// <returns>
        /// The effective, comma-separated resource type(s) to persist on the export job, or <c>null</c> when
        /// the request is unconstrained (either non-SMART, or authorized via a complete system wildcard scope).
        /// </returns>
        string ValidateCreateAccess(CreateExportRequest request);

        /// <summary>
        /// Validates access to an existing export job.
        /// </summary>
        /// <param name="exportJobRecord">The export job.</param>
        void ValidateJobAccess(ExportJobRecord exportJobRecord);
    }
}
