// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Watchdogs
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class SqlMetricsWatchdogConfigurationTests
    {
        [Fact]
        public void GivenSqlMetricsWatchdogConfiguration_WhenBindingFhirServerConfiguration_ThenValuesAreBound()
        {
            var configurationRoot = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    ["FhirServer:Watchdog:SqlMetrics:Enabled"] = "true",
                    ["FhirServer:Watchdog:SqlMetrics:PeriodSeconds"] = "45",
                })
                .Build();

            var configuration = new FhirServerConfiguration();

            configurationRoot.GetSection("FhirServer").Bind(configuration);

            Assert.True(configuration.Watchdog.SqlMetrics.Enabled);
            Assert.Equal(45, configuration.Watchdog.SqlMetrics.PeriodSeconds);
        }
    }
}
