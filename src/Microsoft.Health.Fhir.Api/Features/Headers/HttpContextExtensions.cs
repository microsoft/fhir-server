// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Fhir.Api.Features.Resources;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Persistence.Orchestration;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Api.Features.Headers
{
    public static class HttpContextExtensions
    {
        /// <summary>
        /// Retrieves from the HTTP header if "Latency over efficiency" is enabled.
        /// </summary>
        /// <param name="outerHttpContext">HTTP context</param>
        public static bool IsLatencyOverEfficiencyEnabled(this HttpContext outerHttpContext)
        {
            const bool defaultValue = false;

            if (outerHttpContext == null)
            {
                return defaultValue;
            }

            if (outerHttpContext.Request.Headers.TryGetValue(KnownHeaders.QueryLatencyOverEfficiency, out StringValues headerValues))
            {
                string processingLogicAsString = headerValues.FirstOrDefault();
                if (string.IsNullOrWhiteSpace(processingLogicAsString))
                {
                    return defaultValue;
                }

                if (bool.TryParse(headerValues.ToString().Trim(), out bool result))
                {
                    return result;
                }
            }

            return defaultValue;
        }

        /// <summary>
        /// Retrieves from the HTTP header information on using query caching.
        /// </summary>
        /// <param name="outerHttpContext">HTTP context</param>
        /// <returns>Query cache header value</returns>
        public static string GetQueryCache(this HttpContext outerHttpContext)
        {
            if (outerHttpContext != null && outerHttpContext.Request.Headers.TryGetValue(KnownHeaders.QueryCacheSetting, out StringValues headerValues))
            {
                return headerValues.FirstOrDefault();
            }

            return null;
        }

        /// <summary>
        /// Retrieves from the HTTP header information about the conditional-query processing logic to be adopted.
        /// </summary>
        /// <param name="outerHttpContext">HTTP context</param>
        public static ConditionalQueryProcessingLogic GetConditionalQueryProcessingLogic(this HttpContext outerHttpContext)
        {
            return ExtractEnumerationFlagFromHttpHeader(
                outerHttpContext,
                httpHeaderName: KnownHeaders.ConditionalQueryProcessingLogic,
                defaultValue: ConditionalQueryProcessingLogic.Sequential);
        }

        /// <summary>
        /// Retrieves from the HTTP header information about the bundle processing logic to be adopted.
        /// </summary>
        /// <param name="outerHttpContext">HTTP context</param>
        /// <param name="defaultBundleProcessingLogic">Default bundle processing logic to use if not specified in the header</param>
        public static BundleProcessingLogic GetBundleProcessingLogic(this HttpContext outerHttpContext, BundleProcessingLogic defaultBundleProcessingLogic)
        {
            return ExtractEnumerationFlagFromHttpHeader(
                outerHttpContext,
                httpHeaderName: BundleOrchestratorNamingConventions.HttpHeaderBundleProcessingLogic,
                defaultValue: defaultBundleProcessingLogic);
        }

        public static bool IsBundleProcessingLogicValid(this HttpContext outerHttpContext)
        {
            if (outerHttpContext == null)
            {
                return false;
            }

            if (outerHttpContext.Request.Headers.TryGetValue(BundleOrchestratorNamingConventions.HttpHeaderBundleProcessingLogic, out StringValues headerValues))
            {
                string processingLogicAsString = headerValues.FirstOrDefault();
                if (string.IsNullOrWhiteSpace(processingLogicAsString))
                {
                    // If the bundle processing header is present but empty, we consider it invalid.
                    return false;
                }

                // Check if the value can be parsed as a valid BundleProcessingLogic enum value.
                return Enum.TryParse<BundleProcessingLogic>(processingLogicAsString.Trim(), ignoreCase: true, out BundleProcessingLogic result) && Enum.IsDefined(result);
            }

            // If the bundle processing header is not present, we consider it valid by default.
            return true;
        }

        public static TEnum ExtractEnumerationFlagFromHttpHeader<TEnum>(HttpContext outerHttpContext, string httpHeaderName, TEnum defaultValue)
            where TEnum : struct, Enum
        {
            if (outerHttpContext == null)
            {
                return defaultValue;
            }

            if (outerHttpContext.Request.Headers.TryGetValue(httpHeaderName, out StringValues headerValues))
            {
                string processingLogicAsString = headerValues.FirstOrDefault();
                if (string.IsNullOrWhiteSpace(processingLogicAsString))
                {
                    return defaultValue;
                }

                if (Enum.TryParse(processingLogicAsString.Trim(), ignoreCase: true, out TEnum result) && Enum.IsDefined(result))
                {
                    return result;
                }
            }

            return defaultValue;
        }

        /// <summary>
        /// Decorate FHIR Request Context with a property bag setting queries to use optimized concurrecy.
        /// </summary>
        /// <param name="requestContext">FHIR request context</param>
        public static bool DecorateRequestContextWithOptimizedConcurrency(this IFhirRequestContext requestContext)
        {
            if (requestContext == null)
            {
                return false;
            }

            return requestContext.Properties.TryAdd(KnownQueryParameterNames.OptimizeConcurrency, true);
        }

        public static bool DecorateRequestContextWithQueryCache(this IFhirRequestContext requestContext, string value)
        {
            if (requestContext == null)
            {
                return false;
            }

            return requestContext.Properties.TryAdd(KnownQueryParameterNames.QueryCaching, value);
        }
    }
}
