// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;

namespace Microsoft.Health.Fhir.SqlServer.Features.Operations.Import
{
    public interface ISqlImportOperation
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
        /// Merge resources to resource table.
        /// </summary>
        /// <param name="resources">Input resources content.</param>
        /// <param name="cancellationToken">Cancellation Token</param>
        public Task<IEnumerable<SqlBulkCopyDataWrapper>> BulkMergeResourceAsync(IEnumerable<SqlBulkCopyDataWrapper> resources, CancellationToken cancellationToken);

        /// <summary>
        /// Merge resources to resource and search param tables.
        /// </summary>
        /// <param name="resources">Input resources content.</param>
        /// <param name="cancellationToken">Cancellation Token</param>
        public Task<IEnumerable<ImportResource>> MergeResourcesAsync(IEnumerable<ImportResource> resources, CancellationToken cancellationToken);
    }
}
