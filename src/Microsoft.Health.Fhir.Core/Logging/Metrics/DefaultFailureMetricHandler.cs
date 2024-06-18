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
            _counterErrors = MetricMeter.CreateCounter<long>("Failures.Exceptions");
            _counterHttpErrors = MetricMeter.CreateCounter<long>("Failures.HttpFailures");
        }

        public void EmitException(IExceptionMetricNotification notification)
        {
            EnsureArg.IsNotNull(notification, nameof(notification));

            _counterErrors.Add(
                1,
                KeyValuePair.Create<string, object>("OperationName", notification.OperationName),
                KeyValuePair.Create<string, object>("Severity", notification.Severity),
                KeyValuePair.Create<string, object>("ExceptionType", notification.ExceptionType));
        }

        public void EmitHttpFailure(IHttpFailureMetricNotification notification)
        {
            EnsureArg.IsNotNull(notification, nameof(notification));

            _counterErrors.Add(
                1,
                KeyValuePair.Create<string, object>("OperationName", notification.OperationName));
        }
    }
}
