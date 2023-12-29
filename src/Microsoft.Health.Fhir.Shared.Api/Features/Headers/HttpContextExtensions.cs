// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Fhir.Api.Features.Resources;
using Microsoft.Health.Fhir.Api.Features.Resources.Bundle;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Persistence.Orchestration;

namespace Microsoft.Health.Fhir.Api.Features.Headers
{
    public static class HttpContextExtensions
    {
        /// <summary>
        /// Retrieves from the HTTP header information about the conditional-query processing logic to be adopted.
        /// </summary>
        /// <param name="outerHttpContext">HTTP context</param>
        public static ConditionalQueryProcessingLogic GetConditionalQueryProcessingLogic(this HttpContext outerHttpContext)
        {
            var defaultValue = ConditionalQueryProcessingLogic.Sequential;

            if (outerHttpContext == null)
            {
                return defaultValue;
            }

            if (outerHttpContext.Request.Headers.TryGetValue(KnownHeaders.ConditionalQueryProcessingLogic, out StringValues headerValues))
            {
                string processingLogicAsString = headerValues.FirstOrDefault();
                if (string.IsNullOrWhiteSpace(processingLogicAsString))
                {
                    return defaultValue;
                }

                ConditionalQueryProcessingLogic processingLogic = (ConditionalQueryProcessingLogic)Enum.Parse(typeof(ConditionalQueryProcessingLogic), processingLogicAsString.Trim(), ignoreCase: true);
                return processingLogic;
            }

            return defaultValue;
        }

        /// <summary>
        /// Retrieves from the HTTP header information about the bundle processing logic to be adopted.
        /// </summary>
        /// <param name="outerHttpContext">HTTP context</param>
        public static BundleProcessingLogic GetBundleProcessingLogic(this HttpContext outerHttpContext)
        {
            var defaultValue = BundleProcessingLogic.Sequential;

            if (outerHttpContext == null)
            {
                return defaultValue;
            }

            if (outerHttpContext.Request.Headers.TryGetValue(BundleOrchestratorNamingConventions.HttpHeaderBundleProcessingLogic, out StringValues headerValues))
            {
                string processingLogicAsString = headerValues.FirstOrDefault();
                if (string.IsNullOrWhiteSpace(processingLogicAsString))
                {
                    return defaultValue;
                }

                BundleProcessingLogic processingLogic = (BundleProcessingLogic)Enum.Parse(typeof(BundleProcessingLogic), processingLogicAsString.Trim(), ignoreCase: true);
                return processingLogic;
            }

            return defaultValue;
        }

        public static void DecorateRequestContextWithConditionalQueryProcessingLogic(this FhirRequestContext requestContext, ConditionalQueryProcessingLogic processingLogic)
        {
            requestContext.RequestHeaders.Add(KnownHeaders.ConditionalQueryProcessingLogic, processingLogic.ToString());
        }
    }
}
