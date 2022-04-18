// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Configs;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export.ExportDestinationClient
{
    /// <summary>
    /// Provides functionalities to export resource to a file.
    /// </summary>
    public interface IExportDestinationClient
    {
        /// <summary>
        /// Connects to the destination.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <param name="containerId">The id of the container to use for exporting data. We will use the default/root container if not provided.</param>
        /// <returns>A <see cref="Task"/> representing connection operation.</returns>
        /// <exception cref="DestinationConnectionException">Thrown when we can't connect to the destination.</exception>
        Task ConnectAsync(CancellationToken cancellationToken, string containerId = null);

        /// <summary>
        /// Connects to a destination specified by the given configuration. Must be used in tandom with the CommitAsync that takes an ExportJobConfiguration.
        /// </summary>
        /// <param name="exportJobConfiguration">The job configuration to use for this call.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <param name="containerId">The id of the container to use for exporting data. We will use the default/root container if not provided.</param>
        /// <returns>A <see cref="Task"/> representing connection operation.</returns>
        /// <exception cref="DestinationConnectionException">Thrown when we can't connect to the destination.</exception>
        Task ConnectAsync(ExportJobConfiguration exportJobConfiguration, CancellationToken cancellationToken, string containerId = null);

        /// <summary>
        /// Creates a new file in the destination.
        /// </summary>
        /// <param name="fileName">The file name.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous file creation operation.</returns>
        Task<Uri> CreateFileAsync(string fileName, CancellationToken cancellationToken);

        /// <summary>
        /// Writes part of the file to local data buffer.
        /// </summary>
        /// <param name="fileUri">The URI of the file.</param>
        /// <param name="data">The string to write.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        void WriteFilePartAsync(Uri fileUri, string data, CancellationToken cancellationToken);

        /// <summary>
        /// Commits all changes to storage.
        /// </summary>
        void Commit();

        /// <summary>
        /// Opens an existing file from the destination.
        /// </summary>
        /// <param name="fileUri">Uri of the file to be opened.</param>
        void OpenFileAsync(Uri fileUri);
    }
}
