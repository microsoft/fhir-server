// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Health.Fhir.Core.Models;
using static Hl7.Fhir.Model.Bundle;

namespace Microsoft.Health.Fhir.Api.Features.Resources.Bundle
{
    internal static class BundleHandlerComplexityCalculator
    {
        private const int SearchOperationWeight = 2;
        private const double SequentialExecutionMultiplier = 0.25;

        private const int TotalPayloadWeight = 1;
        private const int LargeResourceWeight = 2;

        private const double MiB = 1024.0 * 1024.0;

        /// <summary>
        /// Identifies the complexity of a bundle based on the scope of its internal operations.
        /// </summary>
        public static BundleComplexity CalculateBundleComplexity(
            BundleProcessingLogic processingLogic,
            Dictionary<HTTPVerb, List<BundleHandlerInternalOperationExecutionContext>> requests,
            IProvideProfilesForValidation profileResolver,
            ILogger logger)
        {
            EnsureArg.IsNotNull(requests, nameof(requests));
            EnsureArg.IsNotNull(profileResolver, nameof(profileResolver));
            EnsureArg.IsNotNull(logger, nameof(logger));

            int requestCount = 0;
            int conditionalOperationCount = 0;
            int searchOperationCount = 0;

            long totalResourceSizeBytes = 0;
            long maxResourceSizeBytes = 0;

            bool requiresProfileRefresh = false;

            IReadOnlySet<string> profileResourceTypes = profileResolver.GetProfilesTypes();

            foreach (BundleHandlerInternalOperationExecutionContext operation in requests.SelectMany(x => x.Value))
            {
                requestCount++;

                if (operation.IsConditionalOperation)
                {
                    conditionalOperationCount++;
                }

                // TODO: GET-by-ID and search operations currently have the same cost.
                // Consider differentiating them when the execution context provides
                // a reliable way to identify search operations.
                if (operation.HttpVerb == HTTPVerb.GET)
                {
                    searchOperationCount++;
                }

                if (!requiresProfileRefresh && operation.ResourceType != null && profileResourceTypes.Contains(operation.ResourceType))
                {
                    requiresProfileRefresh = true;
                }

                long resourceSizeBytes = operation.Context?.HttpContext?.Request?.Body?.Length ?? 0;

                totalResourceSizeBytes += resourceSizeBytes;

                maxResourceSizeBytes = Math.Max(maxResourceSizeBytes, resourceSizeBytes);
            }

            int operationCost = requestCount;

            // Every conditional operation already contributes to operationCost.
            // This represents the additional cost associated with the conditional
            // resolution, making a conditional operation effectively cost 2 points.
            int conditionalCost = conditionalOperationCount;

            int executionCost = CalculateSequentialExecutionCost(
                processingLogic,
                requestCount);

            int profileRefreshCost = CalculateProfileRefreshCost(requiresProfileRefresh);

            int searchCost = CalculateSearchCost(searchOperationCount);

            long payloadCost = CalculatePayloadScore(
                totalResourceSizeBytes,
                maxResourceSizeBytes);

            long score =
                operationCost +
                conditionalCost +
                executionCost +
                searchCost +
                profileRefreshCost +
                payloadCost;

            BundleComplexity bundleComplexity = GetComplexity(score);

            if (bundleComplexity is BundleComplexity.High or BundleComplexity.Extreme)
            {
                logger.LogWarning(
                    "Bundle complexity is {BundleComplexity} with score {ComplexityScore}. " +
                    "RequestCount: {RequestCount}, " +
                    "OperationCost: {OperationCost}, " +
                    "ConditionalOperationCount: {ConditionalOperationCount}, " +
                    "ConditionalCost: {ConditionalCost}, " +
                    "SearchOperationCount: {SearchOperationCount}, " +
                    "SearchCost: {SearchCost}, " +
                    "ExecutionCost: {ExecutionCost}, " +
                    "PayloadCost: {PayloadCost}, " +
                    "TotalResourceSizeBytes: {TotalResourceSizeBytes}, " +
                    "MaxResourceSizeBytes: {MaxResourceSizeBytes}, " +
                    "RequiresProfileRefresh: {RequiresProfileRefresh}, " +
                    "ProcessingLogic: {ProcessingLogic}",
                    bundleComplexity,
                    score,
                    requestCount,
                    operationCost,
                    conditionalOperationCount,
                    conditionalCost,
                    searchOperationCount,
                    searchCost,
                    executionCost,
                    payloadCost,
                    totalResourceSizeBytes,
                    maxResourceSizeBytes,
                    requiresProfileRefresh,
                    processingLogic);
            }

            return bundleComplexity;
        }

        private static int CalculateProfileRefreshCost(bool requiresProfileRefresh)
        {
            const int ProfileRefreshCost = 300;

            return requiresProfileRefresh ? ProfileRefreshCost : 0;
        }

        private static int CalculateSearchCost(int searchOperationCount)
        {
            return searchOperationCount * SearchOperationWeight;
        }

        private static long CalculatePayloadScore(long totalResourceSizeBytes, long maxResourceSizeBytes)
        {
            double totalResourceMiB = totalResourceSizeBytes / MiB;

            double maxResourceMiB = maxResourceSizeBytes / MiB;

            double payloadScore = (totalResourceMiB * TotalPayloadWeight) + (maxResourceMiB * LargeResourceWeight);

            return (long)Math.Ceiling(payloadScore);
        }

        private static int CalculateSequentialExecutionCost(BundleProcessingLogic processingLogic, int internalOperationCount)
        {
            if (processingLogic == BundleProcessingLogic.Parallel)
            {
                return 0;
            }

            return (int)Math.Ceiling(internalOperationCount * SequentialExecutionMultiplier);
        }

        private static BundleComplexity GetComplexity(long score)
        {
            return score switch
            {
                < 100 => BundleComplexity.Low,
                < 500 => BundleComplexity.Medium,
                < 1000 => BundleComplexity.High,
                _ => BundleComplexity.Extreme,
            };
        }
    }
}
