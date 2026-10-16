// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Health.Fhir.Core.Models;
using static Hl7.Fhir.Model.Bundle;

namespace Microsoft.Health.Fhir.Api.Features.Resources.Bundle
{
    internal static class BundleHandlerComplexityCalculator
    {
        private const int ConditionalOperationWeight = 2;
        private const int SearchOperationWeight = 2;

        /// <summary>
        /// Identify the complexity of a bundle based on scope of their internal operations.
        /// </summary>
        public static BundleComplexity CalculateBundleComplexity(
            BundleProcessingLogic processingLogic,
            Dictionary<HTTPVerb, List<BundleHandlerInternalOperationExecutionContext>> requests,
            IProvideProfilesForValidation profileResolver)
        {
            int conditionalOperationCount = requests.SelectMany(r => r.Value).Count(x => x.IsConditionalOperation);

            int searchOperationCount = requests.SelectMany(r => r.Value).Count(x => x.HttpVerb == HTTPVerb.GET);

            int requestCount = requests.SelectMany(r => r.Value).Count();

            IReadOnlySet<string> profileResourceTypes = profileResolver.GetProfilesTypes();
            bool requiresProfileRefresh = requests.SelectMany(r => r.Value).Any(x => x.ResourceType != null && profileResourceTypes.Contains(x.ResourceType));

            long bundleSizeBytes = requests.SelectMany(r => r.Value).Where(x => x.Context?.HttpContext?.Request != null).Select(x => x.Context.HttpContext.Request.ContentLength ?? 0).DefaultIfEmpty(0).Sum();

            long maxResourceSizeBytes = requests.SelectMany(r => r.Value).Where(x => x.Context?.HttpContext?.Request != null).Select(x => x.Context.HttpContext.Request.ContentLength ?? 0).DefaultIfEmpty(0).Max();

            int operationCost = requestCount;

            int conditionalCost = conditionalOperationCount * ConditionalOperationWeight;

            int executionCost = processingLogic == BundleProcessingLogic.Parallel ? 0 : CalculateSequentialExecutionCost(operationCost);

            int searchCost = searchOperationCount * SearchOperationWeight;

            int payloadCost = CalculatePayloadScore(bundleSizeBytes, maxResourceSizeBytes);

            int score = operationCost + conditionalCost + executionCost + searchCost + payloadCost;

            return GetComplexity(score);
        }

        private static int CalculatePayloadScore(long bundleSizeBytes, long maxResourceSizeBytes)
        {
            const long MiB = 1024L * 1024L;

            const int BundleSizeCostPerMiB = 1;
            const int LargeResourceCostPerMiB = 2;

            int bundleSizeCost = (int)Math.Ceiling(bundleSizeBytes / (double)MiB) * BundleSizeCostPerMiB;

            int maxResourceSizeCost = (int)Math.Ceiling(maxResourceSizeBytes / (double)MiB) * LargeResourceCostPerMiB;

            return bundleSizeCost + maxResourceSizeCost;
        }

        private static int CalculateSequentialExecutionCost(int internalOperationCount)
        {
            const double SequentialExecutionMultiplier = 0.25;

            return (int)Math.Ceiling(internalOperationCount * SequentialExecutionMultiplier);
        }

        private static BundleComplexity GetComplexity(int score)
        {
            if (score < 100)
            {
                return BundleComplexity.Low;
            }
            else if (score < 500)
            {
                return BundleComplexity.Medium;
            }
            else if (score < 1000)
            {
                return BundleComplexity.High;
            }
            else
            {
                return BundleComplexity.Extreme;
            }
        }
    }
}
