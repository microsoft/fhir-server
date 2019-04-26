// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Persistence;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export
{
    /// <summary>
    /// Provides mechanism to create a new export job task.
    /// </summary>
    public interface IExportJobTaskFactory
    {
        /// <summary>
        /// Creates a new export job task.
        /// </summary>
        /// <param name="exportJobRecord">The job record.</param>
        /// <param name="weakETag">The version ETag associated with the job record.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A <see cref="Task"/> representing the export job.</returns>
        Task Create(ExportJobRecord exportJobRecord, WeakETag weakETag, CancellationToken cancellationToken);
    }
}
