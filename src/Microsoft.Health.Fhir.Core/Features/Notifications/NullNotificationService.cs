// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Core.Features.Notifications
{
    /// <summary>
    /// No-op implementation of INotificationService for when notifications are disabled.
    /// </summary>
    public class NullNotificationService : INotificationService
    {
        public Task PublishAsync<T>(string channel, T message, CancellationToken cancellationToken = default)
            where T : class
        {
            return Task.CompletedTask;
        }

        public Task SubscribeAsync<T>(string channel, NotificationHandler<T> handler, CancellationToken cancellationToken = default)
            where T : class
        {
            return Task.CompletedTask;
        }

        public Task UnsubscribeAsync(string channel, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
