// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Diagnostics.Metrics;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Logging.Metrics
{
    public sealed class DefaultJobMetricHandler : BaseMeterMetricHandler, IJobMetricHandler
    {
        private readonly Counter<long> _jobCounterExceptions;
        private readonly Counter<long> _jobCompletionCounter;

        public DefaultJobMetricHandler(IMeterFactory metricLogger)
            : base(metricLogger)
        {
            _jobCounterExceptions = MetricMeter.CreateCounter<long>("Job.Exceptions");
            _jobCompletionCounter = MetricMeter.CreateCounter<long>("Job.Completion");
        }

        public void EmitJobException(IJobExceptionMetricNotification notification)
        {
            EnsureArg.IsNotNull(notification, nameof(notification));

            _jobCounterExceptions.Add(
                1,
                KeyValuePair.Create<string, object>("JobType", notification.JobType),
                KeyValuePair.Create<string, object>("Severity", notification.Severity),
                KeyValuePair.Create<string, object>("ExceptionType", notification.ExceptionType));
        }

        public void EmitJobCompletion(IJobCompletionMetricNotification notification)
        {
            EnsureArg.IsNotNull(notification, nameof(notification));

            _jobCompletionCounter.Add(
                (int)notification.Result,
                KeyValuePair.Create<string, object>("JobType", notification.JobType));
        }
    }
}
