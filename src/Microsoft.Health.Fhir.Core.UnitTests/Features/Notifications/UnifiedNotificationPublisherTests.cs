// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Notifications;
using Microsoft.Health.Fhir.Core.Messages.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Notifications
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class UnifiedNotificationPublisherTests
    {
        private readonly IMediator _mediator = Substitute.For<IMediator>();
        private readonly INotificationService _notificationService = Substitute.For<INotificationService>();

        [Fact]
        public void InstanceId_ShouldReturnEnvironmentMachineName()
        {
            // Arrange
            var redisConfig = Options.Create(new RedisConfiguration { Enabled = false });
            var publisher = new UnifiedNotificationPublisher(_mediator, _notificationService, redisConfig, NullLogger<UnifiedNotificationPublisher>.Instance);

            // Act
            var instanceId = publisher.InstanceId;

            // Assert
            Assert.Equal(Environment.MachineName, instanceId);
        }

        [Fact]
        public async Task PublishAsync_WithSingleParameter_ShouldPublishLocally()
        {
            // Arrange
            var redisConfig = Options.Create(new RedisConfiguration { Enabled = false });
            var publisher = new UnifiedNotificationPublisher(_mediator, _notificationService, redisConfig, NullLogger<UnifiedNotificationPublisher>.Instance);
            var notification = new TestMediatRNotification { Message = "test" };

            // Act
            await publisher.PublishAsync(notification, CancellationToken.None);

            // Assert
            await _mediator.Received(1).Publish(notification, Arg.Any<CancellationToken>());
            await _notificationService.DidNotReceive().PublishAsync(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task PublishAsync_WithRedisDisabledAndEnableRedisNotificationFalse_ShouldPublishLocally()
        {
            // Arrange
            var redisConfig = Options.Create(new RedisConfiguration { Enabled = false });
            var publisher = new UnifiedNotificationPublisher(_mediator, _notificationService, redisConfig, NullLogger<UnifiedNotificationPublisher>.Instance);
            var notification = new TestMediatRNotification { Message = "test" };

            // Act
            await publisher.PublishAsync(notification, enableRedisNotification: false, CancellationToken.None);

            // Assert
            await _mediator.Received(1).Publish(notification, Arg.Any<CancellationToken>());
            await _notificationService.DidNotReceive().PublishAsync(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task PublishAsync_WithRedisDisabledButEnableRedisNotificationTrue_ShouldStillPublishLocally()
        {
            // Arrange
            var redisConfig = Options.Create(new RedisConfiguration { Enabled = false });
            var publisher = new UnifiedNotificationPublisher(_mediator, _notificationService, redisConfig, NullLogger<UnifiedNotificationPublisher>.Instance);
            var notification = new TestMediatRNotification { Message = "test" };

            // Act
            await publisher.PublishAsync(notification, enableRedisNotification: true, CancellationToken.None);

            // Assert - Should fall back to local because Redis is disabled
            await _mediator.Received(1).Publish(notification, Arg.Any<CancellationToken>());
            await _notificationService.DidNotReceive().PublishAsync(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task PublishAsync_WithRedisEnabledAndEnableRedisNotificationTrue_ShouldPublishToRedis()
        {
            // Arrange
            var redisConfig = Options.Create(new RedisConfiguration
            {
                Enabled = true,
            });
            var publisher = new UnifiedNotificationPublisher(_mediator, _notificationService, redisConfig, NullLogger<UnifiedNotificationPublisher>.Instance);
            var searchParameters = new List<SearchParameterInfo>
            {
                new SearchParameterInfo("test", "test"),
            };
            var notification = new SearchParametersUpdatedNotification(searchParameters);

            // Act
            await publisher.PublishAsync(notification, enableRedisNotification: true, CancellationToken.None);

            // Assert - Should publish to Redis, not locally
            await _mediator.DidNotReceive().Publish(Arg.Any<INotification>(), Arg.Any<CancellationToken>());
            await _notificationService.Received(1).PublishAsync(
                Arg.Any<string>(),
                Arg.Any<object>(),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task PublishAsync_WithRedisEnabledButEnableRedisNotificationFalse_ShouldPublishLocally()
        {
            // Arrange
            var redisConfig = Options.Create(new RedisConfiguration
            {
                Enabled = true,
            });
            var publisher = new UnifiedNotificationPublisher(_mediator, _notificationService, redisConfig, NullLogger<UnifiedNotificationPublisher>.Instance);
            var searchParameters = new List<SearchParameterInfo>
            {
                new SearchParameterInfo("test", "test"),
            };
            var notification = new SearchParametersUpdatedNotification(searchParameters);

            // Act
            await publisher.PublishAsync(notification, enableRedisNotification: false, CancellationToken.None);

            // Assert - Should publish locally even though Redis is enabled
            await _mediator.Received(1).Publish(notification, Arg.Any<CancellationToken>());
            await _notificationService.DidNotReceive().PublishAsync(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>());
        }

        [Theory]
        [InlineData(true, true)] // Redis enabled, Redis notification enabled -> Redis
        [InlineData(true, false)] // Redis enabled, Redis notification disabled -> Local
        [InlineData(false, true)] // Redis disabled, Redis notification enabled -> Local
        [InlineData(false, false)] // Redis disabled, Redis notification disabled -> Local
        public async Task PublishAsync_BehaviorMatrix_ShouldRouteCorrectly(bool redisEnabled, bool enableRedisNotification)
        {
            // Arrange
            var redisConfig = Options.Create(new RedisConfiguration
            {
                Enabled = redisEnabled,
            });
            var publisher = new UnifiedNotificationPublisher(_mediator, _notificationService, redisConfig, NullLogger<UnifiedNotificationPublisher>.Instance);
            var searchParameters = new List<SearchParameterInfo>
            {
                new SearchParameterInfo("test", "test"),
            };
            var notification = new SearchParametersUpdatedNotification(searchParameters);

            // Act
            await publisher.PublishAsync(notification, enableRedisNotification, CancellationToken.None);

            // Assert
            bool shouldUseRedis = redisEnabled && enableRedisNotification;

            if (shouldUseRedis)
            {
                await _notificationService.Received(1).PublishAsync(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>());
                await _mediator.DidNotReceive().Publish(Arg.Any<INotification>(), Arg.Any<CancellationToken>());
            }
            else
            {
                await _mediator.Received(1).Publish(notification, Arg.Any<CancellationToken>());
                await _notificationService.DidNotReceive().PublishAsync(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>());
            }
        }

        [Fact]
        public async Task PublishAsync_WithNullNotification_ShouldNotThrowAndPassToMediatr()
        {
            // Arrange
            var redisConfig = Options.Create(new RedisConfiguration { Enabled = false });
            var publisher = new UnifiedNotificationPublisher(_mediator, _notificationService, redisConfig, NullLogger<UnifiedNotificationPublisher>.Instance);

            // Act - The UnifiedNotificationPublisher doesn't validate null, it passes through to MediatR
            await publisher.PublishAsync((TestMediatRNotification)null, CancellationToken.None);

            // Assert - Should pass the null to MediatR (MediatR will handle the null)
            await _mediator.Received(1).Publish((TestMediatRNotification)null, Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task PublishAsync_WithCancellationToken_ShouldPassTokenThrough()
        {
            // Arrange
            var redisConfig = Options.Create(new RedisConfiguration { Enabled = false });
            var publisher = new UnifiedNotificationPublisher(_mediator, _notificationService, redisConfig, NullLogger<UnifiedNotificationPublisher>.Instance);
            var notification = new TestMediatRNotification { Message = "test" };
            var cancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = cancellationTokenSource.Token;

            // Act
            await publisher.PublishAsync(notification, cancellationToken);

            // Assert
            await _mediator.Received(1).Publish(notification, cancellationToken);
        }

        [Fact]
        public async Task PublishAsync_WithRedisEnabledAndCancellationToken_ShouldPassTokenToRedis()
        {
            // Arrange
            var redisConfig = Options.Create(new RedisConfiguration
            {
                Enabled = true,
            });
            var publisher = new UnifiedNotificationPublisher(_mediator, _notificationService, redisConfig, NullLogger<UnifiedNotificationPublisher>.Instance);
            var searchParameters = new List<SearchParameterInfo>
            {
                new SearchParameterInfo("test", "test"),
            };
            var notification = new SearchParametersUpdatedNotification(searchParameters);
            var cancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = cancellationTokenSource.Token;

            // Act
            await publisher.PublishAsync(notification, enableRedisNotification: true, cancellationToken);

            // Assert
            await _notificationService.Received(1).PublishAsync(
                Arg.Any<string>(),
                Arg.Any<object>(),
                cancellationToken);
        }

        [Fact]
        public void Constructor_WithNullMediator_ShouldThrowArgumentNullException()
        {
            // Arrange
            var redisConfig = Options.Create(new RedisConfiguration { Enabled = false });

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                new UnifiedNotificationPublisher(null, _notificationService, redisConfig, NullLogger<UnifiedNotificationPublisher>.Instance));
        }

        [Fact]
        public void Constructor_WithNullNotificationService_ShouldThrowArgumentNullException()
        {
            // Arrange
            var redisConfig = Options.Create(new RedisConfiguration { Enabled = false });

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                new UnifiedNotificationPublisher(_mediator, null, redisConfig, NullLogger<UnifiedNotificationPublisher>.Instance));
        }

        [Fact]
        public void Constructor_WithNullRedisConfiguration_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                new UnifiedNotificationPublisher(_mediator, _notificationService, null, NullLogger<UnifiedNotificationPublisher>.Instance));
        }

        [Fact]
        public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
        {
            // Arrange
            var redisConfig = Options.Create(new RedisConfiguration { Enabled = false });

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                new UnifiedNotificationPublisher(_mediator, _notificationService, redisConfig, null));
        }

        [Fact]
        public async Task PublishAsync_WithUnsupportedNotificationType_ShouldThrowNotSupportedException()
        {
            // Arrange
            var redisConfig = Options.Create(new RedisConfiguration { Enabled = true });
            var publisher = new UnifiedNotificationPublisher(_mediator, _notificationService, redisConfig, NullLogger<UnifiedNotificationPublisher>.Instance);
            var notification = new TestMediatRNotification { Message = "test" };

            // Act & Assert - Should throw NotSupportedException for unsupported types when Redis is enabled
            var exception = await Assert.ThrowsAsync<NotSupportedException>(
                () => publisher.PublishAsync(notification, enableRedisNotification: true, CancellationToken.None));

            Assert.Contains("TestMediatRNotification", exception.Message);
            Assert.Contains("does not support Redis publishing", exception.Message);

            // Verify NO publishing occurred since the exception is thrown early
            await _mediator.DidNotReceive().Publish(Arg.Any<TestMediatRNotification>(), Arg.Any<CancellationToken>());
            await _notificationService.DidNotReceive().PublishAsync(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task PublishAsync_WithUnsupportedNotificationTypeButRedisDisabled_ShouldNotThrowAndPublishLocally()
        {
            // Arrange
            var redisConfig = Options.Create(new RedisConfiguration { Enabled = false });
            var publisher = new UnifiedNotificationPublisher(_mediator, _notificationService, redisConfig, NullLogger<UnifiedNotificationPublisher>.Instance);
            var notification = new TestMediatRNotification { Message = "test" };

            // Act - Should not throw when Redis is disabled, even for unsupported types
            await publisher.PublishAsync(notification, enableRedisNotification: true, CancellationToken.None);

            // Assert - Should publish locally instead of throwing
            await _mediator.Received(1).Publish(notification, Arg.Any<CancellationToken>());
            await _notificationService.DidNotReceive().PublishAsync(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task PublishAsync_WithEmptySearchParametersList_ShouldStillPublishToRedis()
        {
            // Arrange
            var redisConfig = Options.Create(new RedisConfiguration
            {
                Enabled = true,
            });
            var publisher = new UnifiedNotificationPublisher(_mediator, _notificationService, redisConfig, NullLogger<UnifiedNotificationPublisher>.Instance);
            var notification = new SearchParametersUpdatedNotification(new List<SearchParameterInfo>());

            // Act
            await publisher.PublishAsync(notification, enableRedisNotification: true, CancellationToken.None);

            // Assert - Should still publish to Redis even with empty list
            await _notificationService.Received(1).PublishAsync(
                Arg.Any<string>(),
                Arg.Any<object>(),
                Arg.Any<CancellationToken>());
            await _mediator.DidNotReceive().Publish(Arg.Any<INotification>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task PublishAsync_RedisPublishingFlow_ShouldUseCorrectChannel()
        {
            // Arrange
            var redisConfig = Options.Create(new RedisConfiguration
            {
                Enabled = true,
            });
            var publisher = new UnifiedNotificationPublisher(_mediator, _notificationService, redisConfig, NullLogger<UnifiedNotificationPublisher>.Instance);
            var searchParameters = new List<SearchParameterInfo>
            {
                new SearchParameterInfo("test-param", "test-param"),
            };
            var notification = new SearchParametersUpdatedNotification(searchParameters);

            // Act
            await publisher.PublishAsync(notification, enableRedisNotification: true, CancellationToken.None);

            // Assert - Should publish to the search parameter updates channel
            await _notificationService.Received(1).PublishAsync(
                "fhir:notifications:searchparameters",
                Arg.Any<object>(),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public void UnifiedNotificationPublisher_ConversionLogic_ShouldHandleKnownTypes()
        {
            // Arrange
            var redisConfig = Options.Create(new RedisConfiguration { Enabled = true });
            var publisher = new UnifiedNotificationPublisher(_mediator, _notificationService, redisConfig, NullLogger<UnifiedNotificationPublisher>.Instance);

            // Act & Assert - The publisher should exist and handle conversion internally
            Assert.NotNull(publisher);
            Assert.Equal(Environment.MachineName, publisher.InstanceId);
        }

        private class TestMediatRNotification : INotification
        {
            public string Message { get; set; }
        }
    }
}
