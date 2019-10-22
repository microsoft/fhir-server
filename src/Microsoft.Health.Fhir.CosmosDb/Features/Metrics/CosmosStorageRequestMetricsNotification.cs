// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Metrics;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Metrics
{
    /// <summary>
    /// A MediatR notification containing statistics about Cosmos DB usage while handling a request.
    /// </summary>
    public class CosmosStorageRequestMetricsNotification : IMetricsNotification
    {
        public CosmosStorageRequestMetricsNotification(string fhirOperation, string resourceType)
        {
            FhirOperation = fhirOperation;
            ResourceType = resourceType;
        }

        /// <summary>
        /// The total RUs consumed in this context.
        /// </summary>
        public double TotalRequestCharge { get; set; }

        /// <summary>
        /// The size of the backing Cosmos DB collection, in kilobytes.
        /// </summary>
        public long? CollectionSizeUsageKilobytes { get; set; }

        /// <summary>
        /// The number of requests that Cosmos DB responded to with an HTTP status code of 429.
        /// </summary>
        public int ThrottledCount { get; set; }

        /// <summary>
        /// The number of requests made to Cosmos DB in this context.
        /// </summary>
        public int RequestCount { get; set; }

        /// <summary>
        /// The FHIR operation being performed.
        /// </summary>
        public string FhirOperation { get; private set; }

        /// <summary>
        /// The type of FHIR resource associated with this context.
        /// </summary>
        public string ResourceType { get; private set; }
    }
}
