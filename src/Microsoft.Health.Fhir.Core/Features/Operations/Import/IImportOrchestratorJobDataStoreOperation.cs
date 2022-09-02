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
        /// <param name="currentProgress">currentProgress</param>
        /// <param name="cancellationToken">Cancellation Token</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1618:Generic type parameters should be documented", Justification = "<Pending>")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1404:Code analysis suppression should have justification", Justification = "<Pending>")]
        public Task PreprocessAsync<T>(IProgress<T> progress, T currentProgress, CancellationToken cancellationToken)
            where T : IndexRebuildProcess;

        /// <summary>
        /// Post-process after import operation.
        /// </summary>
        /// <param name="progress">IProgress</param>
        /// <param name="currentProgress">currentProgress</param>
        /// <param name="cancellationToken">Cancellation Token</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1618:Generic type parameters should be documented", Justification = "<Pending>")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1404:Code analysis suppression should have justification", Justification = "<Pending>")]
        public Task PostprocessAsync<T>(IProgress<T> progress, T currentProgress, CancellationToken cancellationToken)
            where T : IndexRebuildProcess;
    }
}
