// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Context
{
    /// <summary>
    /// A Mediatr notification containing statistics about Cosmos DB usage while handling a request.
    /// This gets emitted by the ApiNotificationMiddleware when a response is returned by the server.
    /// Consume these using Mediatr to collect stats about Cosmos DB usage.
    /// </summary>
    public class CosmosStorageContext : IStorageContext
    {
        public CosmosStorageContext(string fhirOperation, string resourceType)
        {
            FhirOperation = fhirOperation;
            ResourceType = resourceType;
            RequestCount = 0;
        }

        public double TotalRequestCharge { get; set; }

        public long? CollectionSizeUsage { get; set; }

        public int ThrottledCount { get; set; }

        public int RequestCount { get; set; }

        public string FhirOperation { get; private set; }

        public string ResourceType { get; private set; }
    }
}
