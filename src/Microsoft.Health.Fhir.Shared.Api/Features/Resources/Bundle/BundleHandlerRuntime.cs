// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using EnsureThat;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Fhir.Api.Features.Headers;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Models;
using static Hl7.Fhir.Model.Bundle;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.Features.Resources.Bundle
{
    /// <summary>
    /// Set of static methods used as part of the bundle handling logic.
    /// </summary>
    public static class BundleHandlerRuntime
    {
        /// <summary>
        /// Delay logic used in case of retry operations.
        /// </summary>
        public static async Task DelayWithRetryAfterAsync(HttpContext httpContext, CancellationToken cancellationToken)
        {
            int retryDelay = 2;

            var retryAfterValues = httpContext.Response.Headers.GetCommaSeparatedValues("Retry-After");
            if (retryAfterValues != StringValues.Empty && int.TryParse(retryAfterValues[0], out var retryHeaderValue))
            {
                if (retryHeaderValue > 0 && retryHeaderValue <= 30)
                {
                    retryDelay = retryHeaderValue;
                }
            }

            await Task.Delay(retryDelay * 1000, cancellationToken); // multiply by 1000 as retry-header specifies delay in seconds
        }

        /// <summary>
        /// Returns the Bundle Processing Logic to be used for the current request, based on the presence of the Bundle-Processing-Logic header and the validity of its value.
        /// </summary>
        public static BundleProcessingLogic GetBundleProcessingLogic(BundleConfiguration bundleConfiguration, HttpContext outerHttpContext, BundleType? bundleType)
        {
            EnsureArg.IsNotNull(outerHttpContext, nameof(outerHttpContext));

            if (bundleType.HasValue)
            {
                if (bundleType.Value == BundleType.Transaction)
                {
                    // For transactions, the default processing logic is parallel.
                    return outerHttpContext.GetBundleProcessingLogic(bundleConfiguration.TransactionDefaultProcessingLogic);
                }
                else if (bundleType.Value == BundleType.Batch)
                {
                    // For batch, the default processing logic is parallel.
                    return outerHttpContext.GetBundleProcessingLogic(bundleConfiguration.BatchDefaultProcessingLogic);
                }
            }

            // Reaching this part of the code means that the bundle type is not set or it's using an invalid value.
            // Returning sequential as the default processing logic for both cases.
            return BundleProcessingLogic.Sequential;
        }

        /// <summary>
        /// Determines whether a bundle has been cancelled by the client.
        /// If the cancellation is requested and the elapsed time is less than the max bundle execution time, it is assumed that the client cancelled the request.
        /// </summary>
        public static bool IsBundleCancelledByClient(TimeSpan elapsedTime, BundleConfiguration bundleConfiguration, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(bundleConfiguration, nameof(bundleConfiguration));

            return cancellationToken.IsCancellationRequested && elapsedTime.TotalSeconds < bundleConfiguration.MaxExecutionTimeInSeconds;
        }

        internal static bool IsBundleProcessingLogicValid(HttpContext outerHttpContext)
        {
            EnsureArg.IsNotNull(outerHttpContext, nameof(outerHttpContext));

            return outerHttpContext.IsBundleProcessingLogicValid();
        }
    }
}
