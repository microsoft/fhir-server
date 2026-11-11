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
using Microsoft.Health.Fhir.Core.Features.Partitions;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Registration
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Web)]
    public class PartitionConfigurationTests
    {
        [Fact]
        public void GivenNoPartitions_WhenRegisteringTheServer_ThenPartitionRegistryIsEmpty()
        {
            var services = new ServiceCollection();
            services.AddFhirServer();
            using var provider = services.BuildServiceProvider();

            var manager = provider.GetRequiredService<IPartitionManager>();

            Assert.Empty(manager.Partitions);
        }

        [Fact]
        public void GivenPartitionSettings_WhenRegisteringTheServer_ThenSingletonManagerContainsConfiguredPartitions()
        {
            IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
            {
                ["FhirServer:PartitionConfiguration:Partitions:0:Id"] = "partition-a",
                ["FhirServer:PartitionConfiguration:Partitions:0:DatabaseName"] = "database-a",
                ["FhirServer:PartitionConfiguration:Partitions:1:Id"] = "partition-b",
                ["FhirServer:PartitionConfiguration:Partitions:1:DatabaseName"] = "database-b",
            }).Build();
            var services = new ServiceCollection();
            services.AddFhirServer(configuration);
            using var provider = services.BuildServiceProvider();

            var manager = provider.GetRequiredService<IPartitionManager>();

            Assert.Same(manager, provider.GetRequiredService<IPartitionManager>());
            Assert.Equal(2, manager.Partitions.Count);
            Assert.Equal("partition-a", manager.Partitions["partition-a"].Id);
            Assert.Equal("database-a", manager.Partitions["partition-a"].DatabaseName);
            Assert.Equal("database-b", manager.Partitions["partition-b"].DatabaseName);
            Assert.False(manager.Partitions.ContainsKey("PARTITION-A"));
        }

        [Theory]
        [InlineData(null, "database")]
        [InlineData("", "database")]
        [InlineData(" ", "database")]
        [InlineData(".", "database")]
        [InlineData("..", "database")]
        [InlineData("partition/a", "database")]
        [InlineData("partition\\a", "database")]
        [InlineData("partition a", "database")]
        [InlineData("partition%2Fa", "database")]
        [InlineData("partition?a", "database")]
        [InlineData("partition#a", "database")]
        [InlineData("partition-a", null)]
        [InlineData("partition-a", "")]
        [InlineData("partition-a", " ")]
        public void GivenInvalidPartitionSettings_WhenRegisteringTheServer_ThenStartupFails(string id, string databaseName)
        {
            var services = new ServiceCollection();

            var exception = Assert.Throws<InvalidOperationException>(() => services.AddFhirServer(configureAction: config =>
                config.PartitionConfiguration.Partitions.Add(new PartitionInfo { Id = id, DatabaseName = databaseName })));

            Assert.Contains("FhirServer:PartitionConfiguration:Partitions", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void GivenDuplicatePartitionIds_WhenRegisteringTheServer_ThenStartupFails()
        {
            var services = new ServiceCollection();

            Assert.Throws<InvalidOperationException>(() => services.AddFhirServer(configureAction: config =>
            {
                config.PartitionConfiguration.Partitions.Add(new PartitionInfo { Id = "partition-a", DatabaseName = "database-a" });
                config.PartitionConfiguration.Partitions.Add(new PartitionInfo { Id = "partition-a", DatabaseName = "database-b" });
            }));
        }

        [Theory]
        [InlineData("Patient")]
        [InlineData("patient")]
        [InlineData("Account")]
        [InlineData("metadata")]
        [InlineData("_history")]
        [InlineData("CustomError")]
        [InlineData("Operations")]
        [InlineData("OperationDefinition")]
        public void GivenPartitionIdMatchingFhirRoute_WhenRegisteringTheServer_ThenPartitionIsAccepted(string id)
        {
            var services = new ServiceCollection();

            services.AddFhirServer(configureAction: config =>
                config.PartitionConfiguration.Partitions.Add(new PartitionInfo { Id = id, DatabaseName = "database" }));
            using var provider = services.BuildServiceProvider();

            Assert.True(provider.GetRequiredService<IPartitionManager>().Partitions.ContainsKey(id));
        }

        [Theory]
        [InlineData("CosmosDb", 0)]
        [InlineData("CosmosDb", 1)]
        [InlineData("cosmosdb", 2)]
        [InlineData("SqlServer", 1)]
        public void GivenStorageBackend_WhenRegisteringPartitions_ThenUnsupportedPartitionStorageIsRejected(string dataStore, int partitionCount)
        {
            IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
            {
                ["DataStore"] = dataStore,
            }).Build();
            var services = new ServiceCollection();

            var exception = Record.Exception(() => services.AddFhirServer(configuration, configureAction: config =>
            {
                for (int i = 0; i < partitionCount; i++)
                {
                    config.PartitionConfiguration.Partitions.Add(new PartitionInfo { Id = $"partition-{i}", DatabaseName = $"database-{i}" });
                }
            }));

            if (partitionCount > 0 && dataStore.Equals("CosmosDb", StringComparison.OrdinalIgnoreCase))
            {
                Assert.IsType<InvalidOperationException>(exception);
                Assert.Contains("Cosmos", exception.Message, StringComparison.Ordinal);
            }
            else
            {
                Assert.Null(exception);
            }
        }

        [Fact]
        public void GivenNullPartition_WhenRegisteringTheServer_ThenStartupFails()
        {
            var services = new ServiceCollection();

            Assert.Throws<InvalidOperationException>(() => services.AddFhirServer(configureAction: config =>
                config.PartitionConfiguration.Partitions.Add(null)));
        }

        [Fact]
        public void GivenAStartupSnapshot_WhenConfigurationChanges_ThenSupportedPartitionsDoNotChange()
        {
            var configuration = new PartitionConfiguration();
            configuration.Partitions.Add(new PartitionInfo { Id = "partition-a", DatabaseName = "database-a" });
            var manager = new PartitionManager(Options.Create(configuration));

            configuration.Partitions.Clear();
            configuration.Partitions.Add(new PartitionInfo { Id = "partition-b", DatabaseName = "database-b" });

            Assert.Equal("database-a", Assert.Single(manager.Partitions).Value.DatabaseName);
            Assert.False(manager.Partitions.ContainsKey("partition-b"));
        }
    }
}
