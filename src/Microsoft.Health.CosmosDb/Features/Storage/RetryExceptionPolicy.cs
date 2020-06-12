// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using Microsoft.Azure.Cosmos;

namespace Microsoft.Health.CosmosDb.Features.Storage
{
    internal class RetryExceptionPolicy
    {
        /// <summary>
        /// Determines whether the specified exception represents a transient failure that can be compensated by a retry.
        /// </summary>
        /// <param name="ex">The exception object to be verified.</param>
        /// <returns>
        /// true if the specified exception is considered as transient; otherwise, false.
        /// </returns>
        public static bool IsTransient(CosmosException ex)
        {
            // Detects "449 Retry With" - The operation encountered a transient error. This only occurs on write operations. It is safe to retry the operation.
            // Detects "429 Too Many Request" - The collection has exceeded the provisioned throughput limit. Retry the request after the server specified retry after duration.
            // For more information see: https://docs.microsoft.com/en-us/rest/api/documentdb/http-status-codes-for-documentdb
            if (ex.StatusCode == (HttpStatusCode)449 || ex.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return true;
            }

            return false;
        }
    }
}
