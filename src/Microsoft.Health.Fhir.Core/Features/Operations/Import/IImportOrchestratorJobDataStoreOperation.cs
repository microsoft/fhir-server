// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    public interface IImportOrchestratorJobDataStoreOperation
    {
        /// <summary>
        /// Pre-process before import operation.
        /// </summary>
        /// <param name="cancellationToken">Cancellation Token</param>
        public Task PreprocessAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Post-process after import operation.
        /// </summary>
        /// <param name="cancellationToken">Cancellation Token</param>
        public Task PostprocessAsync(CancellationToken cancellationToken);
    }
}
