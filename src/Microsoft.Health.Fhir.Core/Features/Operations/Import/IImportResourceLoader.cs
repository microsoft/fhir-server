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
    /// Loader for import resource
    /// </summary>
    public interface IImportResourceLoader
    {
        /// <summary>
        /// Load import resource to channel.
        /// </summary>
        /// <param name="resourceLocation">resource location</param>
        /// <param name="offset">offset in resource blob/file.</param>
        /// <param name="bytesToRead">number of bytes to read.</param>
        /// <param name="resourceType">FHIR resource type.</param>
        /// <param name="cancellationToken">Cancellation Token. </param>
        public (Channel<ImportResource> resourceChannel, Task loadTask) LoadResources(string resourceLocation, long offset, int bytesToRead, string resourceType, CancellationToken cancellationToken);
    }
}
