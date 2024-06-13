// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Diagnostics.Metrics;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Logging.Metrics
{
    public sealed class DefaultFailureMetricHandler : BaseMeterMetricHandler, IFailureMetricHandler
    {
        private readonly Counter<long> _counterErrors;
        private readonly Counter<long> _counterHttpErrors;

        public DefaultFailureMetricHandler(IMeterFactory meterFactory)
            : base(meterFactory)
        {
            _counterErrors = MetricMeter.CreateCounter<long>("Failures.Errors");
            _counterHttpErrors = MetricMeter.CreateCounter<long>("Failures.HttpErrors");
        }

        public void EmitError(IErrorMetricNotification notification)
        {
            EnsureArg.IsNotNull(notification, nameof(notification));

            _counterErrors.Add(
                1,
                KeyValuePair.Create<string, object>("OperationName", notification.OperationName),
                KeyValuePair.Create<string, object>("Severity", notification.Severity),
                KeyValuePair.Create<string, object>("ExceptionType", notification.ExceptionType));
        }

        public void EmitHttpError(IHttpErrorMetricNotification notification)
        {
            EnsureArg.IsNotNull(notification, nameof(notification));

            _counterErrors.Add(
                1,
                KeyValuePair.Create<string, object>("OperationName", notification.OperationName));
        }
    }
}
