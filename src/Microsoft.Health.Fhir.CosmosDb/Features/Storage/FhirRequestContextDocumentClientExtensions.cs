// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Globalization;
using System.Net;
using EnsureThat;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Health.Abstractions.Exceptions;
using Microsoft.Health.CosmosDb.Features.Storage;
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
                requestContext.AddRequestChargeToFhirRequestContext(
                    responseRequestCharge: dce.RequestCharge,
                    collectionSizeUsage: null,
                    statusCode: dce.StatusCode);

                if (dce.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    throw new RequestRateExceededException(dce.RetryAfter);
                }
                else if (dce.Message.Contains("Invalid Continuation Token", StringComparison.OrdinalIgnoreCase))
                {
                    throw new Core.Exceptions.RequestNotValidException(Core.Resources.InvalidContinuationToken);
                }
                else if (dce.StatusCode == HttpStatusCode.RequestEntityTooLarge)
                {
                    throw new RequestEntityTooLargeException();
                }
            }
        }

        public static void UpdateFhirRequestContext<T>(this IFhirRequestContext requestContext, T resourceResponseBase)
            where T : ResourceResponseBase
        {
            requestContext.UpdateFhirRequestContext(resourceResponseBase.SessionToken, resourceResponseBase.RequestCharge, resourceResponseBase.CollectionSizeUsage, resourceResponseBase.StatusCode);
        }

        public static void UpdateFhirRequestContext<T>(this IFhirRequestContext requestContext, FeedResponse<T> feedResponse)
        {
            requestContext.UpdateFhirRequestContext(feedResponse.SessionToken, feedResponse.RequestCharge, feedResponse.CollectionSizeUsage, statusCode: null);
        }

        public static void UpdateFhirRequestContext<T>(this IFhirRequestContext requestContext, StoredProcedureResponse<T> storedProcedureResponse)
        {
            requestContext.UpdateFhirRequestContext(storedProcedureResponse.SessionToken, storedProcedureResponse.RequestCharge, collectionSizeUsageKilobytes: null, statusCode: storedProcedureResponse.StatusCode);
        }

        /// <summary>
        /// Updates the request context with Cosmos DB info and updates response headers with the session token and request change values.
        /// </summary>
        /// <param name="requestContext">The request context. Allowed to be null.</param>
        /// <param name="sessionToken">THe session token</param>
        /// <param name="responseRequestCharge">The request charge.</param>
        /// <param name="collectionSizeUsageKilobytes">The size usage of the Cosmos collection in kilobytes.</param>
        /// <param name="statusCode">The HTTP status code.</param>
        private static void UpdateFhirRequestContext(this IFhirRequestContext requestContext, string sessionToken, double responseRequestCharge, long? collectionSizeUsageKilobytes, HttpStatusCode? statusCode)
        {
            if (requestContext == null)
            {
                return;
            }

            if (!string.IsNullOrEmpty(sessionToken))
            {
                requestContext.ResponseHeaders[CosmosDbHeaders.SessionToken] = sessionToken;
            }

            requestContext.AddRequestChargeToFhirRequestContext(responseRequestCharge, collectionSizeUsageKilobytes, statusCode);
        }

        private static void AddRequestChargeToFhirRequestContext(this IFhirRequestContext requestContext, double responseRequestCharge, long? collectionSizeUsage, HttpStatusCode? statusCode)
        {
            // If there has already been a request to the database for this request, then there will already by a request charge.
            // We want to update it to the new total.
            // Instead of parsing the header value, we could store the double value on the IFhirRequestContext in addition to storing the header value.
            // The problem with that approach is that the request charge is a Cosmos DB-specific concept and the IFhirRequestContext is independent of data store.
            // Also, at the time of writing, we do not typically issue more than one request to the database per request anyway, so the performance impact should
            // not be felt.

            requestContext.StorageRequestMetrics = requestContext.StorageRequestMetrics ?? new CosmosStorageRequestMetrics(requestContext.AuditEventType, requestContext.ResourceType);

            var cosmosMetrics = requestContext.StorageRequestMetrics as CosmosStorageRequestMetrics;

            if (cosmosMetrics == null)
            {
                return;
            }

            cosmosMetrics.TotalRequestCharge += responseRequestCharge;

            requestContext.ResponseHeaders[CosmosDbHeaders.RequestCharge] = cosmosMetrics.TotalRequestCharge.ToString(CultureInfo.InvariantCulture);

            if (collectionSizeUsage.HasValue)
            {
                cosmosMetrics.CollectionSizeUsageKilobytes = collectionSizeUsage;
            }

            if (statusCode.HasValue && statusCode == HttpStatusCode.TooManyRequests)
            {
                cosmosMetrics.ThrottledCount += 1;
            }

            cosmosMetrics.RequestCount++;
        }
    }
}
