// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using MediatR;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    /// <summary>
    /// A Mediatr message that contains statistics about Cosmos DB operations.
    /// This gets emitted in CosmosFhirDataStore.cs. Consume these using Mediatr
    /// to collect stats about Cosmos DB usage by the server.
    /// </summary>
    public class CosmosQueryNotification : INotification
    {
        public string Operation { get; set; }

        public HttpStatusCode? StatusCode { get; set; }

        public double RequestCharge { get; set; }

        public TimeSpan Latency { get; set; }

        public long? CollectionSizeUsage { get; set; }

        public string ResourceType { get; set; }
    }
}
