// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using MediatR;

namespace Microsoft.Health.Fhir.Core.Features.Notifications
{
    /// <summary>
    /// Unified notification publisher that can optionally broadcast to Redis for cross-instance sync.
    /// This interface extends standard MediatR publishing with Redis-aware capabilities.
    /// </summary>
    public interface IUnifiedNotificationPublisher
    {
        /// <summary>
        /// Gets the unique identifier for this FHIR server instance.
        /// </summary>
        string InstanceId { get; }

        /// <summary>
        /// Publishes a notification locally via MediatR (equivalent to _mediator.Publish).
        /// </summary>
        /// <typeparam name="TNotification">The notification type.</typeparam>
        /// <param name="notification">The notification to publish.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task PublishAsync<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : class, INotification;

        /// <summary>
        /// Publishes a notification with optional Redis cross-instance broadcasting.
        /// </summary>
        /// <typeparam name="TNotification">The notification type.</typeparam>
        /// <param name="notification">The notification to publish.</param>
        /// <param name="enableRedisNotification">Whether to also broadcast via Redis for cross-instance sync.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task PublishAsync<TNotification>(
            TNotification notification,
            bool enableRedisNotification,
            CancellationToken cancellationToken = default)
            where TNotification : class, INotification;
    }
}
