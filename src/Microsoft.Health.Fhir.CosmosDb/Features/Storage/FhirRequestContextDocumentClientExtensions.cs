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
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Context;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    internal static class FhirRequestContextDocumentClientExtensions
    {
        /// <summary>
        /// Adds request charge to the response headers and throws a <see cref="RequestRateExceededException"/>
        /// if the status code is 429.
        /// </summary>
        /// <param name="requestContext">The request context</param>
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

                if (dce.StatusCode == (HttpStatusCode)429)
                {
                    throw new RequestRateExceededException(dce.RetryAfter);
                }
            }
        }

        /// <summary>
        /// Updates the response headers with the session token and request change values.
        /// </summary>
        /// <param name="requestContext">The request context</param>
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
            if (requestContext.ResponseHeaders.TryGetValue(CosmosDbHeaders.RequestCharge, out StringValues existingValues) &&
                double.TryParse(existingValues.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out double existingCharge))
            {
                responseRequestCharge += existingCharge;
            }

            requestContext.ResponseHeaders[CosmosDbHeaders.RequestCharge] = responseRequestCharge.ToString(CultureInfo.InvariantCulture);
        }
    }
}
