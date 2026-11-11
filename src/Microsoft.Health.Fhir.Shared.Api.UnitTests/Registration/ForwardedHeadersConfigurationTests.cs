// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Registration
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Security)]
    public class ForwardedHeadersConfigurationTests
    {
        [Fact]
        public void GivenForwardedHeadersEnabledWithoutTrustedForwarders_WhenResolvingOptions_ThenTrustRestrictionIsCleared()
        {
            using ServiceProvider provider = BuildProvider(new Dictionary<string, string>
            {
                ["ASPNETCORE_FORWARDEDHEADERS_ENABLED"] = "true",
            });

            ForwardedHeadersOptions options = provider.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;

            Assert.Empty(options.KnownProxies);
            Assert.Empty(options.KnownIPNetworks);
        }

        [Fact]
        public void GivenOnlyTrustedProxiesConfigured_WhenResolvingOptions_ThenOnlyConfiguredProxiesAreTrusted()
        {
            using ServiceProvider provider = BuildProvider(new Dictionary<string, string>
            {
                ["ASPNETCORE_FORWARDEDHEADERS_ENABLED"] = "true",
                ["FhirServer:ForwardedHeaders:KnownProxies:0"] = "10.0.0.4",
            });

            ForwardedHeadersOptions options = provider.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;

            Assert.Equal(new[] { IPAddress.Parse("10.0.0.4") }, options.KnownProxies);
            Assert.Empty(options.KnownIPNetworks);
        }

        [Fact]
        public void GivenOnlyTrustedNetworksConfigured_WhenResolvingOptions_ThenProxiesAreClearedAndNetworksAdded()
        {
            using ServiceProvider provider = BuildProvider(new Dictionary<string, string>
            {
                ["ASPNETCORE_FORWARDEDHEADERS_ENABLED"] = "true",
                ["FhirServer:ForwardedHeaders:KnownNetworks:0"] = "10.244.0.0/16",
            });

            ForwardedHeadersOptions options = provider.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;

            Assert.Empty(options.KnownProxies);
            Assert.Single(options.KnownIPNetworks, n => n.BaseAddress.Equals(IPAddress.Parse("10.244.0.0")) && n.PrefixLength == 16);
        }

        [Theory]
        [InlineData("FhirServer:ForwardedHeaders:KnownProxies:0", "not-an-ip")]
        [InlineData("FhirServer:ForwardedHeaders:KnownNetworks:0", "10.0.0.0")]
        [InlineData("FhirServer:ForwardedHeaders:KnownNetworks:0", "10.0.0.0/33")]
        [InlineData("FhirServer:ForwardedHeaders:KnownNetworks:0", "bad/8")]
        public void GivenInvalidTrustedForwarder_WhenBuildingTheServerGraph_ThenStartupFails(string key, string value)
        {
            Assert.Throws<InvalidOperationException>(() => BuildProvider(new Dictionary<string, string>
            {
                ["ASPNETCORE_FORWARDEDHEADERS_ENABLED"] = "true",
                [key] = value,
            }));
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
