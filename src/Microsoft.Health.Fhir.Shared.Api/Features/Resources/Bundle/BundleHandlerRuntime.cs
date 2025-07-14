// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.AspNetCore.Http;
using Microsoft.Health.Fhir.Api.Features.Headers;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Models;
using static Hl7.Fhir.Model.Bundle;

namespace Microsoft.Health.Fhir.Api.Features.Resources.Bundle
{
    /// <summary>
    /// Set of static methods used as part of the bundle handling logic.
    /// </summary>
    public static class BundleHandlerRuntime
    {
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

        internal static bool IsBundleProcessingLogicValid(HttpContext outerHttpContext)
        {
            EnsureArg.IsNotNull(outerHttpContext, nameof(outerHttpContext));

            return outerHttpContext.IsBundleProcessingLogicValid();
        }
    }
}
