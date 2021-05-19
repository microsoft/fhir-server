// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Core.Features.Operations
{
    /// <summary>
    /// Bulk import related store operations
    /// </summary>
    public interface IFhirDataBulkImportOperation
    {
        /// <summary>
        /// Clean resources and params by resource type and sequence id range.
        /// </summary>
        /// <param name="resourceType">FHIR Resource Type</param>
        /// <param name="beginSequenceId">Begin sequence id. </param>
        /// <param name="endSequenceId">End sequence id. </param>
        /// <param name="cancellationToken">Cancellation Token</param>
        public Task CleanBatchResourceAsync(string resourceType, long beginSequenceId, long endSequenceId, CancellationToken cancellationToken);

        /// <summary>
        /// Copy table to data store.
        /// </summary>
        /// <param name="dataTable">Input data table.</param>
        /// <param name="cancellationToken">Cancellation Token</param>
        public Task BulkCopyDataAsync(DataTable dataTable, CancellationToken cancellationToken);

        /// <summary>
        /// Pre-process before import operation.
        /// </summary>
        /// <param name="cancellationToken">Cancellation Token</param>
        public Task PreprocessAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Post-process after import operation.
        /// </summary>
        /// <param name="concurrentCount">Rebuild operation concurrent count. </param>
        /// <param name="cancellationToken">Cancellation Token</param>
        public Task PostprocessAsync(int concurrentCount, CancellationToken cancellationToken);

        /// <summary>
        /// Remove duplicated resoruces in data store.
        /// </summary>
        /// /// <param name="cancellationToken">Cancellation Token</param>
        public Task DeleteDuplicatedResourcesAsync(CancellationToken cancellationToken);
    }
}
