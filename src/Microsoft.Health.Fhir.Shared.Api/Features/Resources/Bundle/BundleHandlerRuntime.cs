// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.AspNetCore.Http;
using Microsoft.Health.Fhir.Api.Features.Headers;
using Microsoft.Health.Fhir.Core.Models;
using static Hl7.Fhir.Model.Bundle;

namespace Microsoft.Health.Fhir.Api.Features.Resources.Bundle
{
    public static class BundleHandlerRuntime
    {
        // The default bundle processing logic for Transactions is set to Parallel, as by the FHIR specification, a transactional bundle
        // cannot contain any resources that refer to each other, and therefore can be processed in parallel.
        // The default bundle processing logic for Batches is set to Sequential, as by the FHIR specification, a batch can contain resources
        // that refer to each other, and therefore must be processed sequentially.
        private const BundleProcessingLogic BatchDefaultBundleProcessingLogic = BundleProcessingLogic.Sequential;
        private const BundleProcessingLogic TransactionDefaultBundleProcessingLogic = BundleProcessingLogic.Parallel;

        public static BundleProcessingLogic GetBundleProcessingLogic(HttpContext outerHttpContext, BundleType? bundleType)
        {
            EnsureArg.IsNotNull(outerHttpContext, nameof(outerHttpContext));

            if (bundleType.HasValue)
            {
                if (bundleType.Value == BundleType.Transaction)
                {
                    // For transactions, the default processing logic is parallel.
                    return outerHttpContext.GetBundleProcessingLogic(TransactionDefaultBundleProcessingLogic);
                }
                else if (bundleType.Value == BundleType.Batch)
                {
                    // For batch, the default processing logic is parallel.
                    return outerHttpContext.GetBundleProcessingLogic(BatchDefaultBundleProcessingLogic);
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
