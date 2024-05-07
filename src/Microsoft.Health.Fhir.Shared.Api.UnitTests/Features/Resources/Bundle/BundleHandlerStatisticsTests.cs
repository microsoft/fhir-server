// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Api.Features.Resources.Bundle;
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

            var bundleStatistics = new BundleHandlerStatistics(Hl7.Fhir.Model.Bundle.BundleType.Batch, BundleProcessingLogic.Parallel, true, 100);

            Parallel.For(0, numberOfResources - 2, i =>
            {
                // Success.
                bundleStatistics.RegisterNewEntry(Hl7.Fhir.Model.Bundle.HTTPVerb.POST, i, "200", TimeSpan.Zero);
            });

            // Customer error.
            bundleStatistics.RegisterNewEntry(Hl7.Fhir.Model.Bundle.HTTPVerb.POST, numberOfResources - 2, "400", TimeSpan.Zero);

            // Server error.
            bundleStatistics.RegisterNewEntry(Hl7.Fhir.Model.Bundle.HTTPVerb.POST, numberOfResources - 1, "500", TimeSpan.Zero);

            Assert.Equal(numberOfResources, bundleStatistics.NumberOfResources);
            Assert.Equal(numberOfResources, bundleStatistics.RegisteredEntries);
            Assert.Equal(BundleProcessingLogic.Parallel, bundleStatistics.BundleProcessingLogic);

            JObject statisticsAsJson = JObject.Parse(bundleStatistics.GetStatisticsAsJson());
            long success = statisticsAsJson["success"].Value<long>();
            long errors = statisticsAsJson["errors"].Value<long>();
            long customerErrors = statisticsAsJson["customerErrors"].Value<long>();

            Assert.Equal(bundleStatistics.RegisteredEntries, success + errors + customerErrors);
        }
    }
}
