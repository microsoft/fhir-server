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

        private readonly ConcurrentBag<BundleHandlerStatisticEntry> _entries;

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
        }

        public int NumberOfResources { get; }

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

        public bool FailedDueClientError { get; private set; }

        public bool Cancelled { get; private set; }

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

            JObject serializableEntity = JObject.FromObject(new
            {
                label = GetLoggingCategory(),
                bundleType = BundleType.ToString(),
                processingLogic = BundleProcessingLogic.ToString(),
                optimizedQuerySet = OptimizedQueryProcessing.ToString(),
                numberOfResources = NumberOfResources,
                registeredEntries = RegisteredEntries,
                executionTime = ElapsedMilliseconds,
                clientError = FailedDueClientError,
                cancelled = Cancelled,
                success = successedRequests,
                errors = failedRequests,
                customerErrors = customerFailedRequests,
                statistics = finalStatistics,
                resourceTypes = resourceTypesStatistics,
                references = new
                {
                    identifiers = GeneratedIdentifiers,
                    references = ResolvedReferences,
                },
            });

            return serializableEntity.ToString();
        }

        public void RegisterNewEntry(Hl7.Fhir.Model.Bundle.HTTPVerb httpVerb, string resourceType, int index, string statusCode, TimeSpan elapsedTime)
        {
            if (!Enum.TryParse(statusCode, out HttpStatusCode httpStatusCode))
            {
                httpStatusCode = HttpStatusCode.BadRequest;
            }

            _entries.Add(new BundleHandlerStatisticEntry() { HttpVerb = httpVerb, ResourceType = resourceType, Index = index, HttpStatusCode = (int)httpStatusCode, ElapsedTime = elapsedTime });
        }

        public void MarkBundleAsFailedDueClientError()
        {
            FailedDueClientError = true;
        }

        public void MarkBundleAsCancelled()
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
        }
    }
}
