// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Registration
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Bundle)]
    public class BundleLimitsConfigurationTests
    {
        [Fact]
        public void GivenNoConfiguration_WhenBuildingTheServerGraph_ThenLimitsUseDefaultsAndEnforcementIsOff()
        {
            using ServiceProvider provider = BuildProvider(new Dictionary<string, string>());

            BundleConfiguration bundle = provider.GetRequiredService<IOptions<BundleConfiguration>>().Value;
            Assert.False(bundle.EnableSizeRestrictions);
            Assert.Equal(50 * 1024 * 1024, bundle.MaxBundleSizeBytes);
            Assert.Equal(10 * 1024 * 1024, bundle.MaxResourceSizeBytes);
            Assert.Equal(100, bundle.MaxSearchIndexValuesPerResource);

            CircuitBreakerConfiguration circuitBreaker = provider.GetRequiredService<IOptions<CircuitBreakerConfiguration>>().Value;
            Assert.False(circuitBreaker.EnableAutoThrottle);
            Assert.Equal(0.8, circuitBreaker.MemoryPressureThreshold);
            Assert.Equal(60, circuitBreaker.ThrottleRecoveryTimeSeconds);
        }

        [Fact]
        public void GivenOverriddenValues_WhenBuildingTheServerGraph_ThenConfiguredValuesAreBound()
        {
            using ServiceProvider provider = BuildProvider(new Dictionary<string, string>
            {
                ["FhirServer:Bundle:EnableSizeRestrictions"] = "true",
                ["FhirServer:Bundle:MaxBundleSizeBytes"] = "0",
                ["FhirServer:Bundle:MaxResourceSizeBytes"] = "20971520",
                ["FhirServer:Bundle:MaxSearchIndexValuesPerResource"] = "2000",
                ["FhirServer:CircuitBreaker:EnableAutoThrottle"] = "true",
                ["FhirServer:CircuitBreaker:MemoryPressureThreshold"] = "0.75",
                ["FhirServer:CircuitBreaker:ThrottleRecoveryTimeSeconds"] = "30",
            });

            BundleConfiguration bundle = provider.GetRequiredService<IOptions<BundleConfiguration>>().Value;
            Assert.True(bundle.EnableSizeRestrictions);
            Assert.Equal(0, bundle.MaxBundleSizeBytes);
            Assert.Equal(20 * 1024 * 1024, bundle.MaxResourceSizeBytes);
            Assert.Equal(2000, bundle.MaxSearchIndexValuesPerResource);

            CircuitBreakerConfiguration circuitBreaker = provider.GetRequiredService<IOptions<CircuitBreakerConfiguration>>().Value;
            Assert.True(circuitBreaker.EnableAutoThrottle);
            Assert.Equal(0.75, circuitBreaker.MemoryPressureThreshold);
            Assert.Equal(30, circuitBreaker.ThrottleRecoveryTimeSeconds);
        }

        [Theory]
        [InlineData("FhirServer:Bundle:MaxBundleSizeBytes", "-1")]
        [InlineData("FhirServer:Bundle:MaxResourceSizeBytes", "-1")]
        [InlineData("FhirServer:Bundle:MaxSearchIndexValuesPerResource", "-1")]
        [InlineData("FhirServer:CircuitBreaker:MemoryPressureThreshold", "0")]
        [InlineData("FhirServer:CircuitBreaker:MemoryPressureThreshold", "1.5")]
        [InlineData("FhirServer:CircuitBreaker:MemoryPressureThreshold", "NaN")]
        [InlineData("FhirServer:CircuitBreaker:ThrottleRecoveryTimeSeconds", "-1")]
        public void GivenAnInvalidValue_WhenBuildingTheServerGraph_ThenStartupFails(string key, string value)
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => BuildProvider(new Dictionary<string, string> { [key] = value }));

            Assert.Contains(key, exception.Message, StringComparison.Ordinal);
        }

        private static ServiceProvider BuildProvider(Dictionary<string, string> values)
        {
            IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
            var services = new ServiceCollection();
            services.AddFhirServer(configuration);
            return services.BuildServiceProvider();
        }
    }
}
