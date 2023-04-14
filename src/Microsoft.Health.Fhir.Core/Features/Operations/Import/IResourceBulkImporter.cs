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
    public interface IResourceBulkImporter
    {
        /// <summary>
        /// Import resource into data store.
        /// </summary>
        /// <param name="inputChannel">Input channel for resource.</param>
        /// <param name="importErrorStore">Import error store.</param>
        /// <param name="cancellationToken">Cancellation Token.</param>
        public (Channel<ImportProcessingProgress> progressChannel, Task importTask) Import(Channel<ImportResource> inputChannel, IImportErrorStore importErrorStore, CancellationToken cancellationToken);
    }
}
