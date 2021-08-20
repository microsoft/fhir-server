// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    /// <summary>
    /// Store for import error
    /// </summary>
    public interface IImportErrorStore
    {
        /// <summary>
        /// Error file location.
        /// </summary>
        public string ErrorFileLocation { get; }

        /// <summary>
        /// Upload import error to store.
        /// </summary>
        /// <param name="importErrors">Import errors in string format.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public Task UploadErrorsAsync(string[] importErrors, CancellationToken cancellationToken);
    }
}
