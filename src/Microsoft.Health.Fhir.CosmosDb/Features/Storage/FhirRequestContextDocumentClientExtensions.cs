// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Globalization;
using System.Net;
using EnsureThat;
using Microsoft.Azure.Documents;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Abstractions.Exceptions;
using Microsoft.Health.CosmosDb.Features.Storage;
using Microsoft.Health.Fhir.Core.Features.Context;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    internal static class FhirRequestContextDocumentClientExtensions
    {
        /// <summary>
        /// Adds request charge to the response headers and throws a <see cref="RequestRateExceededException"/>
        /// if the status code is 429.
        /// </summary>
        /// <param name="requestContext">The request context. Allowed to be null.</param>
        /// <param name="ex">The exception</param>
        public static void ProcessException(this IFhirRequestContext requestContext, Exception ex)
        {
            if (requestContext == null)
            {
                return;
            }

            EnsureArg.IsNotNull(ex, nameof(ex));

            if (ex is DocumentClientException dce)
            {
                requestContext.AddRequestChargeToResponseHeaders(dce.RequestCharge);

                if (dce.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    throw new RequestRateExceededException(dce.RetryAfter);
                }
                else if (dce.Message.Contains("Invalid Continuation Token", StringComparison.OrdinalIgnoreCase))
                {
                    throw new Core.Exceptions.RequestNotValidException(Core.Resources.InvalidContinuationToken);
                }
            }
        }

        /// <summary>
        /// Updates the response headers with the session token and request change values.
        /// </summary>
        /// <param name="requestContext">The request context. Allowed to be null.</param>
        /// <param name="sessionToken">THe session token</param>
        /// <param name="responseRequestCharge">The request charge.</param>
        public static void UpdateResponseHeaders(this IFhirRequestContext requestContext, string sessionToken, double responseRequestCharge)
        {
            if (requestContext == null)
            {
                return;
            }

            if (!string.IsNullOrEmpty(sessionToken))
            {
                requestContext.ResponseHeaders[CosmosDbHeaders.SessionToken] = sessionToken;
            }

            requestContext.AddRequestChargeToResponseHeaders(responseRequestCharge);
        }

        private static void AddRequestChargeToResponseHeaders(this IFhirRequestContext requestContext, double responseRequestCharge)
        {
            // If there has already been a request to the database for this request, then there will already by a request charge.
            // We want to update it to the new total.
            // Instead of parsing the header value, we could store the double value on the IFhirRequestContext in addition to storing the header value.
            // The problem with that approach is that the request charge is a Cosmos DB-specific concept and the IFhirRequestContext is independent of data store.
            // Also, at the time of writing, we do not typically issue more than one request to the database per request anyway, so the performance impact should
            // not be felt.

            if (requestContext.ResponseHeaders.TryGetValue(CosmosDbHeaders.RequestCharge, out StringValues existingValues) &&
                double.TryParse(existingValues.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out double existingCharge))
            {
                responseRequestCharge += existingCharge;
            }

            requestContext.ResponseHeaders[CosmosDbHeaders.RequestCharge] = responseRequestCharge.ToString(CultureInfo.InvariantCulture);
        }
    }
}
