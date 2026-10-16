// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Api.Features.Resources.Bundle;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Resources.Bundle
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Bundle)]
    public sealed class BundleHandlerStatisticsTests
    {
        [Fact]
        public void GivenAnInstanceOfBundleStatistics_WhenRegisteringNewEntriesInParallel_EnsureTheFinalNumberOfEntriesMatch()
        {
            const int numberOfResources = 100;

            var bundleStatistics = new BundleHandlerStatistics(
                Hl7.Fhir.Model.Bundle.BundleType.Batch,
                BundleProcessingLogic.Parallel,
                optimizedQuerySet: true,
                numberOfResources: numberOfResources,
                generatedIdentifiers: 5,
                resolvedReferences: 50);
            bundleStatistics.ThrottlingDelays = 3;

            Parallel.For(0, numberOfResources - 2, i =>
            {
                // Success.
                bundleStatistics.RegisterNewEntry(Hl7.Fhir.Model.Bundle.HTTPVerb.POST, "foo", i, HttpStatusCode.OK, TimeSpan.Zero, isConditionalOperation: true);
            });

            // Customer error.
            bundleStatistics.RegisterNewEntry(Hl7.Fhir.Model.Bundle.HTTPVerb.POST, "bar", numberOfResources - 2, HttpStatusCode.BadRequest, TimeSpan.Zero, isConditionalOperation: false);

            // Server error.
            bundleStatistics.RegisterNewEntry(Hl7.Fhir.Model.Bundle.HTTPVerb.POST, "baz", numberOfResources - 1, HttpStatusCode.InternalServerError, TimeSpan.Zero, isConditionalOperation: false);

            Assert.Equal(numberOfResources, bundleStatistics.NumberOfResources);
            Assert.Equal(50, bundleStatistics.ResolvedReferences);
            Assert.Equal(numberOfResources, bundleStatistics.RegisteredEntries);
            Assert.Equal(BundleProcessingLogic.Parallel, bundleStatistics.BundleProcessingLogic);

            string statisticsAsString = bundleStatistics.GetStatisticsAsJson();
            JObject statisticsAsJson = JObject.Parse(statisticsAsString);
            long success = statisticsAsJson["success"].Value<long>();
            long errors = statisticsAsJson["errors"].Value<long>();
            long customerErrors = statisticsAsJson["customerErrors"].Value<long>();
            long conditionalOperations = statisticsAsJson["conditionalOperations"].Value<long>();
            long throttlingDelays = statisticsAsJson["throttlingDelays"].Value<long>();

            Assert.Equal(bundleStatistics.RegisteredEntries, success + errors + customerErrors);

            // Conditional operations and throttling delays.
            Assert.True(throttlingDelays == 3, $"Expected 3 throttling delays but found {throttlingDelays}.");
            Assert.True(conditionalOperations == numberOfResources - 2, $"Expected {numberOfResources - 2} conditional operations but found {conditionalOperations}.");

            // Generated identifiers.
            int generatedIdentifiers = statisticsAsJson["references"]["identifiers"].Value<int>();
            Assert.Equal(5, generatedIdentifiers);
            Assert.Equal(bundleStatistics.GeneratedIdentifiers, generatedIdentifiers);

            // Resolved references.
            int resolvedReferences = statisticsAsJson["references"]["references"].Value<int>();
            Assert.Equal(50, resolvedReferences);
            Assert.Equal(bundleStatistics.ResolvedReferences, resolvedReferences);
        }

        [Fact]
        public void GivenResourceSizesAndSearchIndexValues_WhenGettingStatistics_ThenBucketsAndTopEntriesAreLogged()
        {
            const long mebibyte = 1024 * 1024;

            var bundleStatistics = new BundleHandlerStatistics(
                Hl7.Fhir.Model.Bundle.BundleType.Transaction,
                BundleProcessingLogic.Parallel,
                optimizedQuerySet: false,
                numberOfResources: 10,
                generatedIdentifiers: 0,
                resolvedReferences: 0);
            bundleStatistics.BundleRequestBodyBytes = 123 * mebibyte;

            long[] resourceSizes = [1 * mebibyte, 6 * mebibyte, 7 * mebibyte, 8 * mebibyte, 9 * mebibyte, 12 * mebibyte, 25 * mebibyte];
            Parallel.For(0, resourceSizes.Length, i => bundleStatistics.RegisterResourceSize(i, "DocumentReference", resourceSizes[i]));

            int[] indexValues = [50, 101, 150, 251, 300, 501, 600, 700];
            Parallel.For(0, indexValues.Length, i => bundleStatistics.RegisterEntrySearchIndexValues(i, "Observation", indexValues[i]));

            JObject statisticsAsJson = JObject.Parse(bundleStatistics.GetStatisticsAsJson());

            Assert.Equal(123 * mebibyte, statisticsAsJson["bundleRequestBodyBytes"].Value<long>());

            JToken resourceSize = statisticsAsJson["resourceSize"];
            Assert.Equal(6, resourceSize["over5MiB"].Value<int>());
            Assert.Equal(2, resourceSize["over10MiB"].Value<int>());
            Assert.Equal(1, resourceSize["over20MiB"].Value<int>());
            Assert.Equal(new long[] { 25 * mebibyte, 12 * mebibyte, 9 * mebibyte, 8 * mebibyte, 7 * mebibyte }, resourceSize["large"].Select(e => e["resourceBytes"].Value<long>()).ToArray());
            Assert.Equal(6, resourceSize["large"][0]["index"].Value<int>());
            Assert.Equal("DocumentReference", resourceSize["large"][0]["resourceType"].Value<string>());

            JToken searchIndex = statisticsAsJson["searchIndex"];
            Assert.Equal(7, searchIndex["over100"].Value<int>());
            Assert.Equal(5, searchIndex["over250"].Value<int>());
            Assert.Equal(3, searchIndex["over500"].Value<int>());
            Assert.Equal(new[] { 700, 600, 501, 300, 251 }, searchIndex["heavy"].Select(e => e["indexValues"].Value<int>()).ToArray());
            Assert.Equal(7, searchIndex["heavy"][0]["index"].Value<int>());
            Assert.Equal("Observation", searchIndex["heavy"][0]["resourceType"].Value<string>());
        }

        [Fact]
        public void GivenNoResourceSizesOrSearchIndexValues_WhenGettingStatistics_ThenEmptySummariesAreLogged()
        {
            var bundleStatistics = new BundleHandlerStatistics(
                Hl7.Fhir.Model.Bundle.BundleType.Batch,
                BundleProcessingLogic.Sequential,
                optimizedQuerySet: false,
                numberOfResources: 1,
                generatedIdentifiers: 0,
                resolvedReferences: 0);

            JObject statisticsAsJson = JObject.Parse(bundleStatistics.GetStatisticsAsJson());

            Assert.Equal(JTokenType.Null, statisticsAsJson["bundleRequestBodyBytes"].Type);
            Assert.Equal(0, statisticsAsJson["resourceSize"]["over5MiB"].Value<int>());
            Assert.Empty(statisticsAsJson["resourceSize"]["large"]);
            Assert.Equal(0, statisticsAsJson["searchIndex"]["over100"].Value<int>());
            Assert.Empty(statisticsAsJson["searchIndex"]["heavy"]);
        }
    }
}
