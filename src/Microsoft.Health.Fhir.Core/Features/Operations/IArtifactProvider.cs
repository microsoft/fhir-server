// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;

namespace Microsoft.Health.Fhir.Core.Features.Operations
{
    public interface IArtifactProvider
    {
        /// <summary>
        /// Fetch artifact used by FHIR server.
        /// </summary>
        /// <param name="exportJobRecord">The export job record. </param>
        /// <param name="targetStream">The stream for target artifact content.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        Task FetchAsync(ExportJobRecord exportJobRecord, Stream targetStream, CancellationToken cancellationToken);
    }
}
