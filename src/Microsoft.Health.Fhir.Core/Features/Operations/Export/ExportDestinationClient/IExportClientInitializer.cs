// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export.ExportDestinationClient
{
    public interface IExportClientInitializer<T>
    {
        /// <summary>
        /// Used to get a client that is authorized to talk to the export destination.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A client of type T</returns>
        /// <exception cref="ExportClientInitializerException">Thrown when unable to initialize client.</exception>
        Task<T> GetAuthorizedClientAsync(CancellationToken cancellationToken);
    }
}
