// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Core.Features.Notifications
{
    /// <summary>
    /// Handler for notification messages.
    /// </summary>
    /// <typeparam name="T">The message type.</typeparam>
    /// <param name="message">The notification message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public delegate Task NotificationHandler<T>(T message, CancellationToken cancellationToken = default)
        where T : class;

    /// <summary>
    /// Service for publishing and subscribing to notifications across FHIR server instances.
    /// </summary>
    public interface INotificationService
    {
        /// <summary>
        /// Publishes a notification to a specific channel.
        /// </summary>
        /// <typeparam name="T">The message type.</typeparam>
        /// <param name="channel">The notification channel.</param>
        /// <param name="message">The notification message.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task PublishAsync<T>(string channel, T message, CancellationToken cancellationToken = default)
            where T : class;

        /// <summary>
        /// Subscribes to notifications on a specific channel.
        /// </summary>
        /// <typeparam name="T">The message type.</typeparam>
        /// <param name="channel">The notification channel.</param>
        /// <param name="handler">The message handler.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task SubscribeAsync<T>(string channel, NotificationHandler<T> handler, CancellationToken cancellationToken = default)
            where T : class;

        /// <summary>
        /// Unsubscribes from notifications on a specific channel.
        /// </summary>
        /// <param name="channel">The notification channel.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task UnsubscribeAsync(string channel, CancellationToken cancellationToken = default);
    }
}
