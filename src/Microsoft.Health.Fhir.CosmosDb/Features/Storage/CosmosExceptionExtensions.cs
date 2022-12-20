// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Globalization;
using System.Net;
using Microsoft.Azure.Cosmos;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    public static class CosmosExceptionExtensions
    {
        /// <summary>
        /// When a javascript stored procedure throws an exception in code, such as
        /// throw new Error(404, "Document not found");
        /// the status code of the exception will be 400 bad request, but there will be a substatus header
        /// with the error code specified in the Error object (404 in this example).
        /// This method returns the substatus code if it is present.
        /// </summary>
        /// <param name="exception">The exception object</param>
        /// <returns>The status code or null if not present (or not an integer)</returns>
        public static HttpStatusCode? GetSubStatusCode(this CosmosException exception)
        {
            return (HttpStatusCode?)exception.GetSubStatusValue();
        }

        /// <summary>
        /// When a javascript stored procedure throws an exception in code, such as
        /// throw new Error(404, "Document not found");
        /// the status code of the exception will be 400 bad request, but there will be a substatus header
        /// with the error code specified in the Error object (404 in this example).
        /// This method returns the value of substatus header if it is present.
        /// </summary>
        /// <param name="exception">The exception object</param>
        /// <returns>The status code value or null if not present (or not an integer)</returns>
        public static int? GetSubStatusValue(this CosmosException exception)
        {
            return GetSubStatusValue(exception.Headers);
        }

        /// <summary>
        /// When a javascript stored procedure throws an exception in code, such as
        /// throw new Error(404, "Document not found");
        /// the status code of the exception will be 400 bad request, but there will be a substatus header
        /// with the error code specified in the Error object (404 in this example).
        /// This method returns the value of substatus header if it is present.
        /// </summary>
        /// <param name="cosmosHeaders">The collection of headers</param>
        /// <returns>The status code value or null if not present (or not an integer)</returns>
        public static int? GetSubStatusValue(this Headers cosmosHeaders)
        {
            return int.TryParse(cosmosHeaders.Get(CosmosDbHeaders.SubStatus), NumberStyles.Integer, CultureInfo.InvariantCulture, out int subStatusCode)
                ? subStatusCode
                : null;
        }

        /// <summary>
        /// Determines if the error is due to a client customer-managed key error
        /// </summary>
        /// <param name="exception">The exception object</param>
        /// <returns>True iff the error is due to client CMK setting.</returns>
        public static bool IsCmkClientError(this CosmosException exception)
        {
            return exception.StatusCode == HttpStatusCode.Forbidden && Enum.IsDefined(typeof(KnownCosmosDbCmkSubStatusValueClientIssue), exception.SubStatusCode);
        }

        /// <summary>
        /// The Cosmos SDK will at times return a 503 error, whith additional info in the
        /// body of the message indicating that the exception is due to a timeout
        /// </summary>
        /// <param name="exception">The exception object</param>
        /// <returns>bool if request timeout found in the body of the message</returns>
        public static bool IsServiceUnavailableDueToTimeout(this CosmosException exception)
        {
            if (exception.StatusCode == HttpStatusCode.ServiceUnavailable
                || exception.StatusCode == HttpStatusCode.RequestTimeout)
            {
                if (exception.Message.Contains("RequestTimeout", StringComparison.OrdinalIgnoreCase) ||
                    exception.InnerException.Message.Contains("RequestTimeout", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }

            return false;
        }
    }
}
