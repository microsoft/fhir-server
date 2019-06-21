// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export.ExportDestinationClient
{
    /// <summary>
    /// Provides functionalities to export resource to a file.
    /// </summary>
    public interface IExportDestinationClient
    {
        /// <summary>
        /// Gets the supported destination type.
        /// </summary>
        string DestinationType { get; }

        /// <summary>
        /// Connects to the destination.
        /// </summary>
        /// <param name="connectionSettings">The connection settings.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <param name="containerId">The id of the container to use for exporting data. We will use the default/root container if not provided.</param>
        /// <returns>A <see cref="Task"/> representing connection operation.</returns>
        Task ConnectAsync(string connectionSettings, CancellationToken cancellationToken, string containerId = null);

        /// <summary>
        /// Creates a new file in the destination.
        /// </summary>
        /// <param name="fileName">The file name.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous file creation operation.</returns>
        Task<Uri> CreateFileAsync(string fileName, CancellationToken cancellationToken);

        /// <summary>
        /// Writes part of the file.
        /// </summary>
        /// <param name="fileUri">The URI of the file.</param>
        /// <param name="partId">The part ID.</param>
        /// <param name="bytes">The bytes array to write.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous write operation.</returns>
        Task WriteFilePartAsync(Uri fileUri, uint partId, byte[] bytes, CancellationToken cancellationToken);

        /// <summary>
        /// Commits the written parts of the file.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous commit operation.</returns>
        Task CommitAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Opens an existing file from the destination.
        /// </summary>
        /// <param name="fileUri">Uri of the file to be opened.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous initialize operation.</returns>
        Task OpenFileAsync(Uri fileUri, CancellationToken cancellationToken);
    }
}
