// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using Hl7.Fhir.Utility;
using Microsoft.Health.Fhir.Core.Logging;
using Microsoft.Health.Fhir.Core.Models;
using Newtonsoft.Json.Linq;
using static Hl7.Fhir.Model.Bundle;

namespace Microsoft.Health.Fhir.Api.Features.Resources.Bundle
{
    public sealed class BundleHandlerStatistics : BaseOperationStatistics
    {
        private const string LoggingCategory = "bundleStatistics";

        private const long OneMebibyte = 1024 * 1024;
        private const long LargeEntryThresholdBytes = 5 * OneMebibyte;
        private const int HeavyEntryThresholdIndexValues = 100;
        private const int TopEntriesToLog = 5;

        private readonly ConcurrentBag<BundleHandlerStatisticEntry> _entries;
        private readonly ConcurrentBag<(int Index, string ResourceType, long Bytes)> _entrySizes;
        private readonly ConcurrentBag<(int Index, string ResourceType, int IndexValues)> _entrySearchIndexValues;

        public BundleHandlerStatistics(
            BundleType? bundleType,
            BundleProcessingLogic bundleProcessingLogic,
            bool optimizedQuerySet,
            int numberOfResources,
            int generatedIdentifiers,
            int resolvedReferences)
            : base()
        {
            BundleType = bundleType;
            BundleProcessingLogic = bundleProcessingLogic;
            OptimizedQueryProcessing = optimizedQuerySet;
            NumberOfResources = numberOfResources;
            GeneratedIdentifiers = generatedIdentifiers;
            ResolvedReferences = resolvedReferences;
            _entries = new ConcurrentBag<BundleHandlerStatisticEntry>();
            _entrySizes = new ConcurrentBag<(int Index, string ResourceType, long Bytes)>();
            _entrySearchIndexValues = new ConcurrentBag<(int Index, string ResourceType, int IndexValues)>();
        }

        public int NumberOfResources { get; }

        /// <summary>
        /// Total size of the bundle request body, in bytes.
        /// </summary>
        public long? TotalBundleBytes { get; set; }

        /// <summary>
        /// Total number of resource identifiers that were generated during the processing of a transactional bundle.
        /// </summary>
        public int GeneratedIdentifiers { get; set; }

        /// <summary>
        /// Total number of references that were successfully resolved during the processing of a transactional bundle.
        /// </summary>
        public int ResolvedReferences { get; set; }

        public int RegisteredEntries => _entries.Count;

        public BundleType? BundleType { get; }

        public BundleProcessingLogic BundleProcessingLogic { get; }

        public bool OptimizedQueryProcessing { get; }

        public bool FailedByClientError { get; private set; }

        public bool Cancelled { get; private set; }

        public int ThrottlingDelays { get; set; }

        public override string GetLoggingCategory() => LoggingCategory;

        public override string GetStatisticsAsJson()
        {
            var finalStatistics = _entries
                .GroupBy(e => string.Concat(e.HttpVerb, " - ", e.HttpStatusCode))
                .Select(g => new { httpVerb = g.First().HttpVerb.ToString(), statusCode = g.First().HttpStatusCode, count = g.Count(), avgExecutionTime = g.Average(r => r.ElapsedTime.TotalMilliseconds), minExecutionTime = g.Min(r => r.ElapsedTime.TotalMilliseconds), maxExecutionTime = g.Max(r => r.ElapsedTime.TotalMilliseconds) })
                .ToArray();

            var resourceTypesStatistics = _entries
                .GroupBy(e => e.ResourceType)
                .Select(g => new { resourceType = g.Key, count = g.Count() })
                .ToArray();

            int failedRequests = _entries.Count(e => e.HttpStatusCode >= 500);
            int customerFailedRequests = _entries.Count(e => e.HttpStatusCode >= 400 && e.HttpStatusCode < 500);
            int successedRequests = _entries.Count(e => e.HttpStatusCode >= 200 && e.HttpStatusCode < 300);
            int conditionalOperations = _entries.Count(e => e.IsConditionalOperation);

            var entrySize = new
            {
                over5MiB = _entrySizes.Count(e => e.Bytes > 5 * OneMebibyte),
                over10MiB = _entrySizes.Count(e => e.Bytes > 10 * OneMebibyte),
                over20MiB = _entrySizes.Count(e => e.Bytes > 20 * OneMebibyte),
                large = _entrySizes
                    .Where(e => e.Bytes > LargeEntryThresholdBytes)
                    .OrderByDescending(e => e.Bytes)
                    .Take(TopEntriesToLog)
                    .Select(e => new { index = e.Index, resourceType = e.ResourceType, bytes = e.Bytes })
                    .ToArray(),
            };

            var searchIndex = new
            {
                over100 = _entrySearchIndexValues.Count(e => e.IndexValues > 100),
                over250 = _entrySearchIndexValues.Count(e => e.IndexValues > 250),
                over500 = _entrySearchIndexValues.Count(e => e.IndexValues > 500),
                heavy = _entrySearchIndexValues
                    .Where(e => e.IndexValues > HeavyEntryThresholdIndexValues)
                    .OrderByDescending(e => e.IndexValues)
                    .Take(TopEntriesToLog)
                    .Select(e => new { index = e.Index, resourceType = e.ResourceType, indexValues = e.IndexValues })
                    .ToArray(),
            };

            JObject serializableEntity = JObject.FromObject(new
            {
                label = GetLoggingCategory(),
                bundleType = BundleType.ToString(),
                processingLogic = BundleProcessingLogic.ToString(),
                optimizedQuerySet = OptimizedQueryProcessing.ToString(),
                numberOfResources = NumberOfResources,
                registeredEntries = RegisteredEntries,
                executionTime = ElapsedMilliseconds,
                clientError = FailedByClientError,
                cancelled = Cancelled,
                success = successedRequests,
                errors = failedRequests,
                customerErrors = customerFailedRequests,
                statistics = finalStatistics,
                resourceTypes = resourceTypesStatistics,
                throttlingDelays = ThrottlingDelays,
                conditionalOperations = conditionalOperations,
                references = new
                {
                    identifiers = GeneratedIdentifiers,
                    references = ResolvedReferences,
                },
                totalBundleBytes = TotalBundleBytes,
                entrySize,
                searchIndex,
            });

            return serializableEntity.ToString();
        }

        public void RegisterNewEntry(Hl7.Fhir.Model.Bundle.HTTPVerb httpVerb, string resourceType, int index, HttpStatusCode httpStatusCode, TimeSpan elapsedTime, bool isConditionalOperation)
        {
            _entries.Add(new BundleHandlerStatisticEntry()
            {
                HttpVerb = httpVerb,
                ResourceType = resourceType,
                Index = index,
                HttpStatusCode = (int)httpStatusCode,
                ElapsedTime = elapsedTime,
                IsConditionalOperation = isConditionalOperation,
            });
        }

        public void SetBundleAsFailedByClientError()
        {
            FailedByClientError = true;
        }

        /// <summary>
        /// Registers the request body size of a bundle entry.
        /// </summary>
        public void RegisterEntrySize(int index, string resourceType, long bytes)
        {
            _entrySizes.Add((index, resourceType, bytes));
        }

        /// <summary>
        /// Registers the number of search index values extracted for a bundle entry's resource.
        /// </summary>
        public void RegisterEntrySearchIndexValues(int index, string resourceType, int indexValues)
        {
            _entrySearchIndexValues.Add((index, resourceType, indexValues));
        }

        public void SetBundleAsCancelled()
        {
            Cancelled = true;
        }

        private sealed class BundleHandlerStatisticEntry
        {
            public Hl7.Fhir.Model.Bundle.HTTPVerb HttpVerb { get; set; }

            public int Index { get; set; }

            public string ResourceType { get; set; }

            public int HttpStatusCode { get; set; }

            public TimeSpan ElapsedTime { get; set; }

            public bool IsConditionalOperation { get; set; }
        }
    }
}
