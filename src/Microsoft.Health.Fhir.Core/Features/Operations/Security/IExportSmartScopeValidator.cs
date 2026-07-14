// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
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
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>Whether the job must be bound to the current SMART compartment.</returns>
        Task<bool> ValidateCreateAccessAsync(CreateExportRequest request, CancellationToken cancellationToken);

        /// <summary>
        /// Validates access to an existing export job.
        /// </summary>
        /// <param name="exportJobRecord">The export job.</param>
        void ValidateJobAccess(ExportJobRecord exportJobRecord);
    }
}
