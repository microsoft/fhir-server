// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    /// <summary>
    /// Importer for ImportResoruce into data store.
    /// </summary>
    public interface IImporter
    {
        /// <summary>
        /// Import resource into data store.
        /// </summary>
        /// <param name="inputChannel">Input channel for resource.</param>
        /// <param name="importErrorStore">Import error store.</param>
        /// <param name="importMode">Import mode.</param>
        /// <param name="allowNegativeVersions">Flag indicating how late arivals are handled.</param>
        /// <param name="eventualConsistency">Flag indicating whether FHIR indexes are updated in the same SQL transaction with Resoure inserts.</param>
        /// <param name="cancellationToken">Cancellation Token.</param>
        public Task<ImportProcessingProgress> Import(Channel<ImportResource> inputChannel, IImportErrorStore importErrorStore, ImportMode importMode, bool allowNegativeVersions, bool eventualConsistency, CancellationToken cancellationToken);
    }
}
