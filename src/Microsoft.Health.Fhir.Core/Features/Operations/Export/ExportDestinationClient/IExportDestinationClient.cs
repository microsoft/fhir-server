// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
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
        /// Writes part of the file to local data buffer.
        /// </summary>
        /// <param name="fileName">The name of the file.</param>
        /// <param name="data">The string to write.</param>
        void WriteFilePart(string fileName, string data);

        /// <summary>
        /// Commits all changes to storage.
        /// </summary>
        /// <returns> A dictionary of URIs to the files commited, keyed by their file names </returns>
        IDictionary<string, Uri> Commit();

        /// <summary>
        /// Commits a single file to storage.
        /// </summary>
        /// <param name="fileName">The name of the file to commit</param>
        /// <returns>The URI to the file commited</returns>
        Uri CommitFile(string fileName);
    }
}
