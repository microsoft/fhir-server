// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Export;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    public interface IDataStore
    {
        Task<UpsertOutcome> UpsertAsync(
            ResourceWrapper resource,
            WeakETag weakETag,
            bool allowCreate,
            bool keepHistory,
            CancellationToken cancellationToken);

        Task<ResourceWrapper> GetAsync(ResourceKey key, CancellationToken cancellationToken);

        Task HardDeleteAsync(ResourceKey key, CancellationToken cancellationToken);

        Task<HttpStatusCode> UpsertExportJobAsync(ExportJobRecord jobRecord, CancellationToken cancellationToken);

        Task<ExportJobRecord> GetExportJobAsync(string jobId, CancellationToken cancellationToken);
    }
}
