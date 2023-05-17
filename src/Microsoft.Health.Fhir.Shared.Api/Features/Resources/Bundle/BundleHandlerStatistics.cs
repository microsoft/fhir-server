﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using Hl7.Fhir.Utility;
using Newtonsoft.Json.Linq;
using static Hl7.Fhir.Model.Bundle;

namespace Microsoft.Health.Fhir.Api.Features.Resources.Bundle
{
    internal sealed class BundleHandlerStatistics
    {
        private readonly Stopwatch _stopwatch;

        private readonly List<BundleHandlerStatisticEntry> _entries;

        public BundleHandlerStatistics(BundleType? bundleType, BundleProcessingLogic processingLogic, int numberOfResources)
        {
            BundleType = bundleType;
            ProcessingLogic = processingLogic;
            NumberOfResources = numberOfResources;
            _entries = new List<BundleHandlerStatisticEntry>();

            _stopwatch = new Stopwatch();
        }

        public int NumberOfResources { get; }

        public BundleType? BundleType { get; }

        public BundleProcessingLogic ProcessingLogic { get; }

        public IReadOnlyList<BundleHandlerStatisticEntry> Entries
        {
            get { return _entries; }
        }

        public void StartCollectingResults()
        {
            _stopwatch.Start();
        }

        public void StopCollectingResults()
        {
            _stopwatch.Stop();
        }

        public string GetStatisticsAsJson()
        {
            var finalStatistics = _entries
                .GroupBy(e => string.Concat(e.HttpVerb, " - ", e.HttpStatusCode))
                .Select(g => new { request = g.Key, count = g.Count(), avgExecutionTime = g.Average(r => r.ElapsedTime.TotalMilliseconds), maxExecutionTime = g.Max(r => r.ElapsedTime.TotalMilliseconds) })
                .ToArray();

            int failedRequests = _entries.Count(e => e.HttpStatusCode >= 500);
            int customerFailedRequests = _entries.Count(e => e.HttpStatusCode >= 400 && e.HttpStatusCode < 499);
            int successedRequests = _entries.Count(e => e.HttpStatusCode >= 200 && e.HttpStatusCode < 299);

            JObject serializableEntity = JObject.FromObject(new
            {
                label = "bundleStatistics",
                bundleType = BundleType,
                processingLogic = ProcessingLogic,
                numberOfResources = NumberOfResources,
                executionTime = _stopwatch.ElapsedMilliseconds,
                success = successedRequests,
                errors = failedRequests,
                customerErrors = customerFailedRequests,
                statistics = finalStatistics,
            });

            return serializableEntity.ToString();
        }

        public void RegisterNewEntry(Hl7.Fhir.Model.Bundle.HTTPVerb httpVerb, int index, string statusCode, TimeSpan elapsedTime)
        {
            if (!Enum.TryParse(statusCode, out HttpStatusCode httpStatusCode))
            {
                httpStatusCode = HttpStatusCode.BadRequest;
            }

            _entries.Add(new BundleHandlerStatisticEntry() { HttpVerb = httpVerb, Index = index, HttpStatusCode = (int)httpStatusCode, ElapsedTime = elapsedTime });
        }

#pragma warning disable CA1034 // Nested types should not be visible
        internal sealed class BundleHandlerStatisticEntry
#pragma warning restore CA1034 // Nested types should not be visible
        {
            public Hl7.Fhir.Model.Bundle.HTTPVerb HttpVerb { get; set; }

            public int Index { get; set; }

            public int HttpStatusCode { get; set; }

            public TimeSpan ElapsedTime { get; set; }
        }
    }
}
