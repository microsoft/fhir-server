// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using EnsureThat;
using Hl7.Fhir.Model;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Messages.Upsert;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.Features.Logging
{
    /// <summary>
    /// Logs when a resource is upserted
    /// </summary>
    public class LogResourceUpsertedEvent : INotificationHandler<ResourceUpsertedEvent>
    {
        private readonly ILogger _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="LogResourceUpsertedEvent" /> class.
        /// </summary>
        /// <param name="loggerFactory">The logger factory.</param>
        public LogResourceUpsertedEvent(ILoggerFactory loggerFactory)
        {
            EnsureArg.IsNotNull(loggerFactory, nameof(loggerFactory));

            _logger = loggerFactory.CreateLogger<Resource>();
        }

        /// <inheritdoc />
        public Task Handle(ResourceUpsertedEvent notification, CancellationToken cancellationToken)
        {
            _logger.LogDebug($"{notification.Resource.TypeName}/{notification.Resource.Id} was {notification.Outcome}");

            return Task.CompletedTask;
        }
    }
}
