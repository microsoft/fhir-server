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
                : (int?)null;
        }

        /// <summary>
        /// Determines if the error is due to a client customer-managed key error
        /// </summary>
        /// <param name="exception">The exception object</param>
        /// <returns>True iff the error is due to client CMK setting.</returns>
        public static bool IsCmkClientError(this CosmosException exception)
        {
            // NOTE: It has been confirmed that a SubStatusCode of value '3', although not listed in
            // https://docs.microsoft.com/en-us/rest/api/cosmos-db/http-status-codes-for-cosmosdb#substatus-codes-for-end-user-issues
            // as a possible CMK SubStatusCode by Cosmos DB, has been acknowledged as a possible value in some scenarios if the custtomer has disabled their key.
            return exception.StatusCode == HttpStatusCode.Forbidden
                && (Enum.IsDefined(typeof(KnownCosmosDbCmkSubStatusValueClientIssue), exception.SubStatusCode) || exception.SubStatusCode == 3);
        }
    }
}
