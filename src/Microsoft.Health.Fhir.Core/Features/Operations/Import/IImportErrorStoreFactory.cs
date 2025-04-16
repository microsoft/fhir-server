// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    /// <summary>
    /// Factory for import error store.
    /// </summary>
    public interface IImportErrorStoreFactory
    {
        /// <summary>
        /// Initialize error store with default container name.
        /// </summary>
        /// <param name="fileName">Error file name.</param>
        /// <param name="cancellationToken">Cancellation Token.</param>
        public Task<IImportErrorStore> InitializeAsync(string fileName, CancellationToken cancellationToken);

        /// <summary>
        /// Initialize error store with specified container name.
        /// </summary>
        /// <param name="containerName">Container name for storing error files.</param>
        /// <param name="fileName">Error file name.</param>
        /// <param name="cancellationToken">Cancellation Token.</param>
        public Task<IImportErrorStore> InitializeAsync(string containerName, string fileName, CancellationToken cancellationToken);
    }
}
