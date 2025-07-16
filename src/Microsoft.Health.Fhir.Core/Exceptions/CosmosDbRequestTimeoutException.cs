// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Abstractions.Exceptions;

namespace Microsoft.Health.Fhir.Core.Exceptions
{
    /// <summary>
    /// Exception thrown when Cosmos DB requests timeout due to insufficient provisioned throughput (RUs) or high load.
    /// This extends RequestRateExceededException to return an appropriate status code (429 Too Many Requests) client applications,
    /// Code 408 Request Timeout is not used as is signifies a client timeout.
    /// Code 424 Failed Dependency was considered but would be new behavior for clients.
    /// while providing specific messaging for timeout scenarios.
    /// </summary>
    public class CosmosDbRequestTimeoutException : RequestRateExceededException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="CosmosDbRequestTimeoutException"/> class.
        /// Uses the predefined resource message for Cosmos DB request timeouts.
        /// </summary>
        public CosmosDbRequestTimeoutException()
            : base(retryAfter: null)
        {
        }

        /// <summary>
        /// Gets the message that describes the current exception.
        /// </summary>
        public override string Message => Resources.CosmosDbRequestTimeout;
    }
}
