// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Core.Features.Operations
{
    public interface IFhirDataBulkImportOperation
    {
        public Task CleanBatchResourceAsync(long startSurrogateId, long endSurrogateId, CancellationToken cancellationToken);

        public Task BulkCopyDataAsync(DataTable dataTable, CancellationToken cancellationToken);

        public Task DisableIndexesAsync(CancellationToken cancellationToken);

        public Task RebuildIndexesAsync(int concurrentCount, CancellationToken cancellationToken);

        public Task DeleteDuplicatedResourcesAsync(CancellationToken cancellationToken);
    }
}
