// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Notifications;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Notifications
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class RedisNotificationServiceTests
    {
        [Fact]
        public async Task PublishAsync_WithRedisDisabled_ShouldNotThrowAndCompleteGracefully()
        {
            // Arrange
            var config = Options.Create(new RedisConfiguration { Enabled = false });
            var service = new RedisNotificationService(config, NullLogger<RedisNotificationService>.Instance);
            var testMessage = new TestNotification { Message = "test" };

            // Act & Assert - Should not throw
            await service.PublishAsync("test-channel", testMessage, CancellationToken.None);
        }

        [Fact]
        public void Constructor_WithRedisDisabled_ShouldNotThrow()
        {
            // Arrange & Act
            var config = Options.Create(new RedisConfiguration { Enabled = false });

            // Assert - Should not throw
            var service = new RedisNotificationService(config, NullLogger<RedisNotificationService>.Instance);
            Assert.NotNull(service);
        }

        [Fact]
        public async Task PublishAsync_WithNullMessage_ShouldThrowArgumentException()
        {
            // Arrange
            var config = Options.Create(new RedisConfiguration { Enabled = false });
            var service = new RedisNotificationService(config, NullLogger<RedisNotificationService>.Instance);

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(
                () => service.PublishAsync("test-channel", (TestNotification)null, CancellationToken.None));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public async Task PublishAsync_WithInvalidChannel_ShouldThrowArgumentException(string invalidChannel)
        {
            // Arrange
            var config = Options.Create(new RedisConfiguration { Enabled = false });
            var service = new RedisNotificationService(config, NullLogger<RedisNotificationService>.Instance);
            var testMessage = new TestNotification { Message = "test" };

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(
                () => service.PublishAsync(invalidChannel, testMessage, CancellationToken.None));
        }

        [Fact]
        public async Task SubscribeAsync_WithRedisDisabled_ShouldNotThrowAndCompleteGracefully()
        {
            // Arrange
            var config = Options.Create(new RedisConfiguration { Enabled = false });
            var service = new RedisNotificationService(config, NullLogger<RedisNotificationService>.Instance);
            var handlerCalled = false;
            NotificationHandler<TestNotification> handler = (msg, ct) =>
            {
                handlerCalled = true;
                return Task.CompletedTask;
            };

            // Act & Assert - Should not throw
            await service.SubscribeAsync("test-channel", handler, CancellationToken.None);

            // Verify handler wasn't called (since Redis is disabled)
            Assert.False(handlerCalled);
        }

        [Fact]
        public async Task UnsubscribeAsync_WithRedisDisabled_ShouldNotThrowAndCompleteGracefully()
        {
            // Arrange
            var config = Options.Create(new RedisConfiguration { Enabled = false });
            var service = new RedisNotificationService(config, NullLogger<RedisNotificationService>.Instance);

            // Act & Assert - Should not throw
            await service.UnsubscribeAsync("test-channel", CancellationToken.None);
        }

        [Fact]
        public void Dispose_MultipleCalls_ShouldNotThrow()
        {
            // Arrange
            var config = Options.Create(new RedisConfiguration { Enabled = false });
            var service = new RedisNotificationService(config, NullLogger<RedisNotificationService>.Instance);

            // Act & Assert - Should not throw on multiple disposes
            service.Dispose();
            service.Dispose();
            service.Dispose();
        }

        [Fact]
        public async Task PublishAsync_WithValidParameters_ShouldCompleteSuccessfully()
        {
            // Arrange
            var config = Options.Create(new RedisConfiguration
            {
                Enabled = false, // Disabled to avoid actual Redis connection
                Host = "localhost",
            });
            var service = new RedisNotificationService(config, NullLogger<RedisNotificationService>.Instance);
            var testMessage = new TestNotification
            {
                Message = "test message",
                Timestamp = DateTimeOffset.UtcNow,
            };

            // Act & Assert - Should complete without throwing
            await service.PublishAsync("valid-channel", testMessage, CancellationToken.None);
        }

        [Fact]
        public async Task SubscribeAsync_WithValidParameters_ShouldCompleteSuccessfully()
        {
            // Arrange
            var config = Options.Create(new RedisConfiguration
            {
                Enabled = false, // Disabled to avoid actual Redis connection
                Host = "localhost",
            });
            var service = new RedisNotificationService(config, NullLogger<RedisNotificationService>.Instance);
            var handlerCalled = false;
            NotificationHandler<TestNotification> handler = (msg, ct) =>
            {
                handlerCalled = true;
                return Task.CompletedTask;
            };

            // Act & Assert - Should complete without throwing
            await service.SubscribeAsync("valid-channel", handler, CancellationToken.None);

            // Verify handler wasn't called (since Redis is disabled)
            Assert.False(handlerCalled);
        }

        [Fact]
        public void Constructor_WithRedisConfigurationOptions_ShouldApplyAllSettings()
        {
            // Arrange
            var config = Options.Create(new RedisConfiguration
            {
                Enabled = false,
                Host = "test-host",
                InstanceName = "test-instance",
                SearchParameterNotificationDelayMs = 15000,
            });

            // Act & Assert - Should not throw and handle all configuration properly
            var service = new RedisNotificationService(config, NullLogger<RedisNotificationService>.Instance);
            Assert.NotNull(service);

            // Dispose to clean up
            service.Dispose();
        }

        [Fact]
        public void RedisConfiguration_NotificationChannels_ShouldHaveDefaultValues()
        {
            // Arrange
            var config = new RedisConfiguration();

            // Act & Assert
            Assert.NotNull(config.NotificationChannels);
            Assert.Equal("fhir:notifications:searchparameters", config.NotificationChannels.SearchParameterUpdates);
        }

        [Fact]
        public void RedisConnectionConfiguration_ShouldHaveDefaultValues()
        {
            // Arrange
            var config = new RedisConfiguration();

            // Act & Assert
            Assert.NotNull(config.Configuration);
            Assert.False(config.Configuration.AbortOnConnectFail);
            Assert.Equal(3, config.Configuration.ConnectRetry);
            Assert.Equal(5000, config.Configuration.ConnectTimeout);
            Assert.Equal(5000, config.Configuration.SyncTimeout);
            Assert.Equal(5000, config.Configuration.AsyncTimeout);
        }

        private class TestNotification
        {
            public string Message { get; set; }

            public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        }
    }
}
