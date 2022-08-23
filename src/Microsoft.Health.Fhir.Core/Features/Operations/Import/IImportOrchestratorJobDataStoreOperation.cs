// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    public interface IImportOrchestratorJobDataStoreOperation
    {
        /// <summary>
        /// Pre-process before import operation.
        /// </summary>
        /// <param name="progress">IProgress</param>
        /// <param name="importOrchestratorJobResult">ImportOrchestratorJobResult</param>
        /// <param name="cancellationToken">Cancellation Token</param>
        public Task PreprocessAsync(IProgress<string> progress, ImportOrchestratorJobResult importOrchestratorJobResult, CancellationToken cancellationToken);

        /// <summary>
        /// Post-process after import operation.
        /// </summary>
        /// <param name="progress">IProgress</param>
        /// <param name="importOrchestratorJobResult">ImportOrchestratorJobResult</param>
        /// <param name="cancellationToken">Cancellation Token</param>
        public Task PostprocessAsync(IProgress<string> progress, ImportOrchestratorJobResult importOrchestratorJobResult, CancellationToken cancellationToken);
    }
}
