// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex.Models;
using Microsoft.Health.Fhir.Core.Features.Persistence;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Reindex
{
    public interface IReindexJobTask
    {
        /// <summary>
        /// Executes the reindex job task.
        /// </summary>
        /// <param name="reindexJobRecord">The reindex job record.</param>
        /// <param name="weakETag">The version ETag.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A <see cref="Task"/> representing the reindex job task.</returns>
        Task ExecuteAsync(ReindexJobRecord reindexJobRecord, WeakETag weakETag, CancellationToken cancellationToken);
    }
}
