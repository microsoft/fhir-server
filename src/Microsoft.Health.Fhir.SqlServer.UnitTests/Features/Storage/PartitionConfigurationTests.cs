// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Features.Partitions;
using Microsoft.Health.Fhir.Core.Registration;
using Microsoft.Health.Fhir.SqlServer.Features.Partitions;
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
        private const string PartitionsSection = "FhirSqlSettings:PartitionConfiguration:Partitions";

        [Fact]
        public void GivenSqlSettings_WhenRegisteringSqlServer_ThenManagerContainsConfiguredPartitions()
        {
            // Arrange
            using ServiceProvider provider = CreateServiceProvider(CreateConfiguration(new Dictionary<string, string>
            {
                [$"{PartitionsSection}:partition-a:Database"] = "database-a",
                [$"{PartitionsSection}:partition-b:Database"] = "database-b",
            }));

            // Act
            var manager = provider.GetRequiredService<IPartitionManager>();
            var sqlManager = provider.GetRequiredService<SqlPartitionManager>();

            // Assert
            Assert.Same(sqlManager, manager);
            Assert.True(manager.IsEnabled);
            AssertPartition(manager, "partition-a", "database-a");
            AssertPartition(manager, "partition-b", "database-b");
            Assert.False(manager.TryGetPartition("PARTITION-A", out IPartitionInfo partition));
            Assert.Null(partition);
        }

        [Fact]
        public void GivenNoPartitionSettings_WhenRegisteringSqlServer_ThenPartitioningIsDisabled()
        {
            // Arrange
            using ServiceProvider provider = CreateServiceProvider(CreateConfiguration(new Dictionary<string, string>()));

            // Act
            var manager = provider.GetRequiredService<IPartitionManager>();

            // Assert
            Assert.False(manager.IsEnabled);
            Assert.False(manager.TryGetPartition("partition-a", out IPartitionInfo partition));
            Assert.Null(partition);
        }

        [Fact]
        public void GivenInvalidSqlSettings_WhenResolvingManager_ThenConfigurationIsRejected()
        {
            // Arrange
            using ServiceProvider provider = CreateServiceProvider(CreateConfiguration(new Dictionary<string, string>
            {
                [$"{PartitionsSection}:partition-a:Database"] = " ",
            }));

            // Act
            var exception = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IPartitionManager>());

            // Assert
            Assert.Equal(typeof(DatabasePartitioningOptions), exception.OptionsType);
            Assert.Contains("partition-a", Assert.Single(exception.Failures), StringComparison.Ordinal);
        }

        [Fact]
        public void GivenConfigurationReload_WhenManagerWasCreated_ThenPartitionsDoNotChange()
        {
            // Arrange
            IConfigurationRoot configuration = CreateConfiguration(new Dictionary<string, string>
            {
                [$"{PartitionsSection}:partition-a:Database"] = "database-a",
            });
            using ServiceProvider provider = CreateServiceProvider(configuration);
            var manager = provider.GetRequiredService<IPartitionManager>();

            // Act
            configuration[$"{PartitionsSection}:partition-a:Database"] = "database-changed";
            configuration[$"{PartitionsSection}:partition-b:Database"] = "database-b";
            configuration.Reload();

            // Assert
            AssertPartition(manager, "partition-a", "database-a");
            Assert.False(manager.TryGetPartition("partition-b", out _));
        }

        [Theory]
        [InlineData("partition-a")]
        [InlineData("Partition_A.1~")]
        [InlineData("Patient")]
        [InlineData("metadata")]
        [InlineData("_history")]
        public void GivenValidPartition_WhenValidatingOptions_ThenOptionsAreAccepted(string id)
        {
            // Arrange
            DatabasePartitioningOptions options = CreateOptions(id, "database");

            // Act
            List<ValidationResult> results = Validate(options);

            // Assert
            Assert.Empty(results);
        }

        [Theory]
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
        public void GivenInvalidPartition_WhenValidatingOptions_ThenOptionsAreRejected(string id, string database)
        {
            // Arrange
            DatabasePartitioningOptions options = CreateOptions(id, database);

            // Act
            List<ValidationResult> results = Validate(options);

            // Assert
            ValidationResult result = Assert.Single(results);
            Assert.Equal(nameof(DatabasePartitioningOptions.Partitions), Assert.Single(result.MemberNames));
        }

        [Fact]
        public void GivenNullPartition_WhenValidatingOptions_ThenOptionsAreRejected()
        {
            // Arrange
            var options = new DatabasePartitioningOptions
            {
                Partitions = new Dictionary<string, PartitionOptions> { ["partition-a"] = null },
            };

            // Act
            List<ValidationResult> results = Validate(options);

            // Assert
            Assert.Single(results);
        }

        private static DatabasePartitioningOptions CreateOptions(string id, string database) =>
            new() { Partitions = new Dictionary<string, PartitionOptions> { [id] = new PartitionOptions { Database = database } } };

        private static List<ValidationResult> Validate(DatabasePartitioningOptions options)
        {
            var results = new List<ValidationResult>();
            Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);
            return results;
        }

        private static IConfigurationRoot CreateConfiguration(Dictionary<string, string> settings) =>
            new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        private static ServiceProvider CreateServiceProvider(IConfiguration configuration)
        {
            var services = new ServiceCollection();
            services.AddSingleton(configuration);
            var builder = Substitute.For<IFhirServerBuilder>();
            builder.Services.Returns(services);
            builder.AddSqlServer(_ => { });
            return services.BuildServiceProvider();
        }

        private static void AssertPartition(IPartitionManager manager, string id, string database)
        {
            Assert.True(manager.TryGetPartition(id, out IPartitionInfo partition));
            var sqlPartition = Assert.IsType<SqlPartitionInfo>(partition);
            Assert.Equal(id, sqlPartition.Id);
            Assert.Equal(database, sqlPartition.Database);
        }
    }
}
