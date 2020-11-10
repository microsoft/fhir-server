// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Core.Features.Operations
{
    public interface IArtifactProvider
    {
        /// <summary>
        /// Fetch artifect used by FHIR server.
        /// </summary>
        /// <param name="location">The location of the artifect. The location string format depends on the artifact provider.</param>
        /// <param name="targetStream">The stream for target artifact content.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        Task FetchAsync(string location, Stream targetStream, CancellationToken cancellationToken);
    }
}
