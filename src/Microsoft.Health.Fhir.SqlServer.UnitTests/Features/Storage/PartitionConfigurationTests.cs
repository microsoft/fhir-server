// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Features.Partitions;
using Microsoft.Health.Fhir.Core.Registration;
using Microsoft.Health.Fhir.SqlServer.Features.Partitions;
using Microsoft.Health.Fhir.SqlServer.Registration;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Storage
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    public class PartitionConfigurationTests
    {
        [Fact]
        public void GivenNoSqlPartitionSettings_WhenRegisteringSqlServer_ThenRegistryIsEmpty()
        {
            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
            var builder = Substitute.For<IFhirServerBuilder>();
            builder.Services.Returns(services);
            builder.AddSqlServer(_ => { });
            using var provider = services.BuildServiceProvider();

            Assert.Empty(provider.GetRequiredService<IPartitionManager>().Partitions);
        }

        [Fact]
        public void GivenSqlSettings_WhenRegisteringSqlServer_ThenManagerContainsConfiguredPartitions()
        {
            IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
            {
                ["FhirSqlSettings:PartitionConfiguration:Partitions:0:Id"] = "partition-a",
                ["FhirSqlSettings:PartitionConfiguration:Partitions:0:DatabaseName"] = "database-a",
                ["FhirSqlSettings:PartitionConfiguration:Partitions:1:Id"] = "partition-b",
                ["FhirSqlSettings:PartitionConfiguration:Partitions:1:DatabaseName"] = "database-b",
            }).Build();
            var services = new ServiceCollection();
            services.AddSingleton(configuration);
            var builder = Substitute.For<IFhirServerBuilder>();
            builder.Services.Returns(services);
            builder.AddSqlServer(_ => { });
            using var provider = services.BuildServiceProvider();

            var manager = provider.GetRequiredService<IPartitionManager>();
            var sqlManager = provider.GetRequiredService<PartitionManager>();

            Assert.Same(sqlManager, manager);
            Assert.Equal(2, manager.Partitions.Count);
            Assert.Contains("partition-a", manager.Partitions);
            Assert.DoesNotContain("PARTITION-A", manager.Partitions);
            Assert.True(sqlManager.TryGetDatabaseName("partition-a", out string database));
            Assert.Equal("database-a", database);
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
        public void GivenInvalidPartition_WhenCreatingManager_ThenConfigurationIsRejected(string id, string databaseName)
        {
            var configuration = new PartitionConfiguration();
            configuration.Partitions.Add(new PartitionInfo { Id = id, DatabaseName = databaseName });
            var exception = Assert.Throws<InvalidOperationException>(() => new PartitionManager(Options.Create(configuration)));
            Assert.Contains("FhirSqlSettings:PartitionConfiguration:Partitions", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void GivenDuplicateIds_WhenCreatingManager_ThenConfigurationIsRejected()
        {
            var configuration = new PartitionConfiguration();
            configuration.Partitions.Add(new PartitionInfo { Id = "partition-a", DatabaseName = "database-a" });
            configuration.Partitions.Add(new PartitionInfo { Id = "partition-a", DatabaseName = "database-b" });
            Assert.Throws<InvalidOperationException>(() => new PartitionManager(Options.Create(configuration)));
        }

        [Fact]
        public void GivenNullPartition_WhenCreatingManager_ThenConfigurationIsRejected()
        {
            var configuration = new PartitionConfiguration();
            configuration.Partitions.Add(null);
            Assert.Throws<InvalidOperationException>(() => new PartitionManager(Options.Create(configuration)));
        }

        [Fact]
        public void GivenConfigurationChanges_WhenManagerWasCreated_ThenSnapshotDoesNotChange()
        {
            var configuration = new PartitionConfiguration();
            configuration.Partitions.Add(new PartitionInfo { Id = "partition-a", DatabaseName = "database-a" });
            var manager = new PartitionManager(Options.Create(configuration));
            configuration.Partitions.Clear();
            configuration.Partitions.Add(new PartitionInfo { Id = "partition-b", DatabaseName = "database-b" });

            Assert.Equal("partition-a", Assert.Single(manager.Partitions));
            Assert.True(manager.TryGetDatabaseName("partition-a", out string database));
            Assert.Equal("database-a", database);
        }

        [Theory]
        [InlineData("Patient")]
        [InlineData("metadata")]
        [InlineData("_history")]
        public void GivenPartitionIdMatchingRoute_WhenCreatingManager_ThenIdIsAccepted(string id)
        {
            var configuration = new PartitionConfiguration();
            configuration.Partitions.Add(new PartitionInfo { Id = id, DatabaseName = "database" });
            var manager = new PartitionManager(Options.Create(configuration));

            Assert.Contains(id, manager.Partitions);
        }
    }
}
