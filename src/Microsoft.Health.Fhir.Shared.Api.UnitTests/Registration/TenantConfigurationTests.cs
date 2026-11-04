// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Tenants;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Registration
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Web)]
    public class TenantConfigurationTests
    {
        [Fact]
        public void GivenNoTenants_WhenRegisteringTheServer_ThenTenantRegistryIsEmpty()
        {
            var services = new ServiceCollection();
            services.AddFhirServer();
            using var provider = services.BuildServiceProvider();

            var manager = provider.GetRequiredService<ITenantManager>();

            Assert.Empty(manager.Tenants);
        }

        [Fact]
        public void GivenTenantSettings_WhenRegisteringTheServer_ThenSingletonManagerContainsConfiguredTenants()
        {
            IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
            {
                ["FhirServer:TenantConfiguration:Tenants:0:Id"] = "tenant-a",
                ["FhirServer:TenantConfiguration:Tenants:0:DatabaseName"] = "database-a",
                ["FhirServer:TenantConfiguration:Tenants:1:Id"] = "tenant-b",
                ["FhirServer:TenantConfiguration:Tenants:1:DatabaseName"] = "database-b",
            }).Build();
            var services = new ServiceCollection();
            services.AddFhirServer(configuration);
            using var provider = services.BuildServiceProvider();

            var manager = provider.GetRequiredService<ITenantManager>();

            Assert.Same(manager, provider.GetRequiredService<ITenantManager>());
            Assert.Equal(2, manager.Tenants.Count);
            Assert.Equal("tenant-a", manager.Tenants["tenant-a"].Id);
            Assert.Equal("database-a", manager.Tenants["tenant-a"].DatabaseName);
            Assert.Equal("database-b", manager.Tenants["tenant-b"].DatabaseName);
            Assert.False(manager.Tenants.ContainsKey("TENANT-A"));
        }

        [Theory]
        [InlineData(null, "database")]
        [InlineData("", "database")]
        [InlineData(" ", "database")]
        [InlineData(".", "database")]
        [InlineData("..", "database")]
        [InlineData("tenant/a", "database")]
        [InlineData("tenant\\a", "database")]
        [InlineData("tenant a", "database")]
        [InlineData("tenant%2Fa", "database")]
        [InlineData("tenant?a", "database")]
        [InlineData("tenant#a", "database")]
        [InlineData("tenant-a", null)]
        [InlineData("tenant-a", "")]
        [InlineData("tenant-a", " ")]
        public void GivenInvalidTenantSettings_WhenRegisteringTheServer_ThenStartupFails(string id, string databaseName)
        {
            var services = new ServiceCollection();

            var exception = Assert.Throws<InvalidOperationException>(() => services.AddFhirServer(configureAction: config =>
                config.TenantConfiguration.Tenants.Add(new TenantInfo { Id = id, DatabaseName = databaseName })));

            Assert.Contains("FhirServer:TenantConfiguration:Tenants", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void GivenDuplicateTenantIds_WhenRegisteringTheServer_ThenStartupFails()
        {
            var services = new ServiceCollection();

            Assert.Throws<InvalidOperationException>(() => services.AddFhirServer(configureAction: config =>
            {
                config.TenantConfiguration.Tenants.Add(new TenantInfo { Id = "tenant-a", DatabaseName = "database-a" });
                config.TenantConfiguration.Tenants.Add(new TenantInfo { Id = "tenant-a", DatabaseName = "database-b" });
            }));
        }

        [Fact]
        public void GivenNullTenant_WhenRegisteringTheServer_ThenStartupFails()
        {
            var services = new ServiceCollection();

            Assert.Throws<InvalidOperationException>(() => services.AddFhirServer(configureAction: config =>
                config.TenantConfiguration.Tenants.Add(null)));
        }

        [Fact]
        public void GivenAStartupSnapshot_WhenConfigurationChanges_ThenSupportedTenantsDoNotChange()
        {
            var configuration = new TenantConfiguration();
            configuration.Tenants.Add(new TenantInfo { Id = "tenant-a", DatabaseName = "database-a" });
            var manager = new TenantManager(Options.Create(configuration));

            configuration.Tenants.Clear();
            configuration.Tenants.Add(new TenantInfo { Id = "tenant-b", DatabaseName = "database-b" });

            Assert.Equal("database-a", Assert.Single(manager.Tenants).Value.DatabaseName);
            Assert.False(manager.Tenants.ContainsKey("tenant-b"));
        }
    }
}
