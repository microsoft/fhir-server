// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
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

            Parallel.For(0, numberOfResources - 2, i =>
            {
                // Success.
                bundleStatistics.RegisterNewEntry(Hl7.Fhir.Model.Bundle.HTTPVerb.POST, "foo", i, "200", TimeSpan.Zero);
            });

            // Customer error.
            bundleStatistics.RegisterNewEntry(Hl7.Fhir.Model.Bundle.HTTPVerb.POST, "bar", numberOfResources - 2, "400", TimeSpan.Zero);

            // Server error.
            bundleStatistics.RegisterNewEntry(Hl7.Fhir.Model.Bundle.HTTPVerb.POST, "baz", numberOfResources - 1, "500", TimeSpan.Zero);

            Assert.Equal(numberOfResources, bundleStatistics.NumberOfResources);
            Assert.Equal(50, bundleStatistics.ResolvedReferences);
            Assert.Equal(numberOfResources, bundleStatistics.RegisteredEntries);
            Assert.Equal(BundleProcessingLogic.Parallel, bundleStatistics.BundleProcessingLogic);

            string statisticsAsString = bundleStatistics.GetStatisticsAsJson();
            JObject statisticsAsJson = JObject.Parse(statisticsAsString);
            long success = statisticsAsJson["success"].Value<long>();
            long errors = statisticsAsJson["errors"].Value<long>();
            long customerErrors = statisticsAsJson["customerErrors"].Value<long>();

            Assert.Equal(bundleStatistics.RegisteredEntries, success + errors + customerErrors);

            // Generated identifiers.
            int generatedIdentifiers = statisticsAsJson["references"]["identifiers"].Value<int>();
            Assert.Equal(5, generatedIdentifiers);
            Assert.Equal(bundleStatistics.GeneratedIdentifiers, generatedIdentifiers);

            // Resolved references.
            int resolvedReferences = statisticsAsJson["references"]["references"].Value<int>();
            Assert.Equal(50, resolvedReferences);
            Assert.Equal(bundleStatistics.ResolvedReferences, resolvedReferences);
        }
    }
}
