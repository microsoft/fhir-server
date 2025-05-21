// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.JobManagement;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.SqlServer.Features.Storage;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Storage
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    public class SqlQueueClientTests
    {
        private readonly ISqlRetryService _sqlRetryService;
        private readonly ILogger<SqlQueueClient> _logger;
        private readonly SchemaInformation _schemaInformation;

        public SqlQueueClientTests()
        {
            _sqlRetryService = Substitute.For<ISqlRetryService>();
            _logger = Substitute.For<ILogger<SqlQueueClient>>();
            _schemaInformation = new SchemaInformation(1, 1) { Current = 1 };
        }

        [Fact]
        public void GivenNoConfigurationSection_WhenCreatingClient_ThenDefaultTimeoutsAreUsed()
        {
            // Arrange - create a SqlQueueClient with no configuration
            var sqlQueueClient = new SqlQueueClient(_schemaInformation, _sqlRetryService, _logger);

            // Act - Use reflection to access the private field for configuration
            var configField = typeof(SqlQueueClient).GetField("_queueClientConfiguration", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var config = (SqlQueueClientConfiguration)configField.GetValue(sqlQueueClient);

            // Assert - Verify that default values are used
            Assert.Equal(TimeSpan.FromMinutes(5), config.CommandTimeout);
            Assert.Equal(TimeSpan.FromMinutes(5), config.DequeueTimeout);
            Assert.Equal(TimeSpan.FromMinutes(5), config.EnqueueTimeout);
            Assert.Equal(TimeSpan.FromMinutes(5), config.CompleteJobTimeout);
        }

        [Fact]
        public void GivenEmptyConfigurationSection_WhenCreatingClient_ThenDefaultTimeoutsAreUsed()
        {
            // Arrange - create an empty configuration
            var options = Options.Create(new SqlQueueClientConfiguration());
            var sqlQueueClient = new SqlQueueClient(_schemaInformation, _sqlRetryService, _logger, options);

            // Act - Use reflection to access the private field for configuration
            var configField = typeof(SqlQueueClient).GetField("_queueClientConfiguration", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var config = (SqlQueueClientConfiguration)configField.GetValue(sqlQueueClient);

            // Assert - Verify that default values are used
            Assert.Equal(TimeSpan.FromMinutes(5), config.CommandTimeout);
            Assert.Equal(TimeSpan.FromMinutes(5), config.DequeueTimeout);
            Assert.Equal(TimeSpan.FromMinutes(5), config.EnqueueTimeout);
            Assert.Equal(TimeSpan.FromMinutes(5), config.CompleteJobTimeout);
        }

        [Fact]
        public void GivenCustomConfigurationSection_WhenCreatingClient_ThenCustomTimeoutsAreUsed()
        {
            // Arrange - create a custom configuration
            var customConfig = new SqlQueueClientConfiguration
            {
                CommandTimeout = TimeSpan.FromMinutes(10),
                DequeueTimeout = TimeSpan.FromMinutes(8),
                EnqueueTimeout = TimeSpan.FromMinutes(7),
                CompleteJobTimeout = TimeSpan.FromMinutes(6),
            };
            var options = Options.Create(customConfig);
            var sqlQueueClient = new SqlQueueClient(_schemaInformation, _sqlRetryService, _logger, options);

            // Act - Use reflection to access the private field for configuration
            var configField = typeof(SqlQueueClient).GetField("_queueClientConfiguration", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var config = (SqlQueueClientConfiguration)configField.GetValue(sqlQueueClient);

            // Assert - Verify that custom values are used
            Assert.Equal(TimeSpan.FromMinutes(10), config.CommandTimeout);
            Assert.Equal(TimeSpan.FromMinutes(8), config.DequeueTimeout);
            Assert.Equal(TimeSpan.FromMinutes(7), config.EnqueueTimeout);
            Assert.Equal(TimeSpan.FromMinutes(6), config.CompleteJobTimeout);
        }

        [Fact]
        public void GivenNullOptions_WhenCreatingClientWithSecondConstructor_ThenDefaultTimeoutsAreUsed()
        {
            // Arrange/Act - create a SqlQueueClient with null IOptions using the second constructor
            var sqlQueueClient = new SqlQueueClient(_sqlRetryService, _logger, null);

            // Act - Use reflection to access the private field for configuration
            var configField = typeof(SqlQueueClient).GetField("_queueClientConfiguration", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var config = (SqlQueueClientConfiguration)configField.GetValue(sqlQueueClient);

            // Assert - Verify that default values are used
            Assert.Equal(TimeSpan.FromMinutes(5), config.CommandTimeout);
            Assert.Equal(TimeSpan.FromMinutes(5), config.DequeueTimeout);
            Assert.Equal(TimeSpan.FromMinutes(5), config.EnqueueTimeout);
            Assert.Equal(TimeSpan.FromMinutes(5), config.CompleteJobTimeout);
        }
    }
}
