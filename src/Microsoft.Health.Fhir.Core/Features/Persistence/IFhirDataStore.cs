// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    public interface IFhirDataStore
    {
        Task<UpsertOutcome> UpsertAsync(
            ResourceWrapper resource,
            WeakETag weakETag,
            bool allowCreate,
            bool keepHistory,
            CancellationToken cancellationToken);

        Task<ResourceWrapper> GetAsync(ResourceKey key, CancellationToken cancellationToken);

        Task HardDeleteAsync(ResourceKey key, CancellationToken cancellationToken);

        Task<ExportJobOutcome> CreateExportJobAsync(ExportJobRecord jobRecord, CancellationToken cancellationToken);

        Task<ExportJobOutcome> GetExportJobAsync(string jobId, CancellationToken cancellationToken);

        Task<ExportJobOutcome> ReplaceExportJobAsync(ExportJobRecord jobRecord, WeakETag eTag, CancellationToken cancellationToken);
    }
}
