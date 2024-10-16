// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Messages.Export;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Extensions
{
    public static class FhirMediatorExtensions
    {
        public static async Task PublishNotificationWithExceptionHandling(this IMediator mediator, string metricName, object notification, ILogger logger, CancellationToken cancellationToken)
        {
            try
            {
                await mediator.Publish(notification, cancellationToken);
            }
            catch (ObjectDisposedException ode)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    logger.LogWarning(ode, $"ObjectDisposedException. Unable to publish {metricName} metric. Cancellation was requested.");
                }
                else
                {
                    logger.LogCritical(ode, $"ObjectDisposedException. Unable to publish {metricName} metric.");
                }
            }
            catch (OperationCanceledException oce)
            {
                logger.LogWarning(oce, $"OperationCanceledException. Unable to publish {metricName} metric. Cancellation was requested.");
            }
            catch (Exception ex)
            {
                logger.LogCritical(ex, $"Unable to publish {metricName} metric.");
            }
        }
    }
}
