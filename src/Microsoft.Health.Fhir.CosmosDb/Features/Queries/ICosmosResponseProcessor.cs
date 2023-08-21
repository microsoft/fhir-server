// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Queries
{
    public interface ICosmosResponseProcessor
    {
        Task ProcessErrorResponseAsync(CosmosResponseMessage response, CancellationToken cancellationToken);

        Task ProcessErrorResponseAsync(HttpStatusCode statusCode, Headers headers, string errorMessage, CancellationToken cancellationToken);

        Task ProcessResponseAsync(CosmosResponseMessage responseMessage, CancellationToken cancellationToken);
    }
}
