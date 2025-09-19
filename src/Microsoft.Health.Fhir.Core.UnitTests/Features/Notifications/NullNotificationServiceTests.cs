// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Notifications;
using Microsoft.Health.Fhir.Core.Features.Notifications.Models;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Notifications
{
    public class NullNotificationServiceTests
    {
        private readonly NullNotificationService _notificationService;

        public NullNotificationServiceTests()
        {
            _notificationService = new NullNotificationService();
        }

        [Fact]
        public async Task PublishAsync_ShouldCompleteSuccessfully()
        {
            // Arrange
            var notification = new SearchParameterChangeNotification
            {
                InstanceId = "test-instance",
                Timestamp = DateTimeOffset.UtcNow,
                ChangeType = SearchParameterChangeType.Created,
                AffectedParameterUris = new[] { "http://hl7.org/fhir/SearchParameter/test" },
                TriggerSource = "test",
            };

            // Act & Assert
            await _notificationService.PublishAsync("test-channel", notification);
        }

        [Fact]
        public async Task SubscribeAsync_ShouldCompleteSuccessfully()
        {
            // Arrange
            Task Handler(SearchParameterChangeNotification notification, CancellationToken cancellationToken = default)
            {
                return Task.CompletedTask;
            }

            // Act & Assert
            await _notificationService.SubscribeAsync<SearchParameterChangeNotification>("test-channel", Handler);
        }

        [Fact]
        public async Task UnsubscribeAsync_ShouldCompleteSuccessfully()
        {
            // Act & Assert
            await _notificationService.UnsubscribeAsync("test-channel");
        }
    }
}
