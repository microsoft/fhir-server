// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Health.Core;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex.Models;
using Microsoft.Health.Fhir.CosmosDb.Features.Metrics;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Operations.Reindex
{
    public class ReindexJobCosmosThrottleController : IReindexJobThrottleController, INotificationHandler<CosmosStorageRequestMetricsNotification>
    {
        private int? _configuredRUThroughput;
        private DateTimeOffset? _intervalStart = null;
        private double _rUsConsumedDuringInterval = 0.0;
        private ushort _delayFactor = 0;
        private double? _targetRUs = null;
        private bool _initialized = false;

        public ReindexJobCosmosThrottleController(int? configuredRUThroughput)
        {
            _configuredRUThroughput = configuredRUThroughput;
        }

        public ReindexJobRecord ReindexJobRecord { get; set; } = null;

        public void Initialize(ReindexJobRecord reindexJobRecord)
        {
            EnsureArg.IsNotNull(reindexJobRecord, nameof(reindexJobRecord));

            ReindexJobRecord = reindexJobRecord;

            if (ReindexJobRecord.TargetDataStoreResourcePercentage.HasValue
                && ReindexJobRecord.TargetDataStoreResourcePercentage.Value > 0
                && _configuredRUThroughput.HasValue
                && _configuredRUThroughput > 0)
            {
                _targetRUs = _configuredRUThroughput.Value * (ReindexJobRecord.TargetDataStoreResourcePercentage.Value / 100.0);
                _delayFactor = 0;
                _rUsConsumedDuringInterval = 0.0;
                _initialized = true;
            }
        }

        public int GetThrottleBasedDelay()
        {
            if (!_initialized)
            {
                // not initialized
                return 0;
            }

            return ReindexJobRecord.QueryDelayIntervalInMilliseconds * _delayFactor;
        }

        public Task Handle(CosmosStorageRequestMetricsNotification notification, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested || notification == null || notification.FhirOperation == null)
            {
                return Task.CompletedTask;
            }

            if (notification.FhirOperation.Equals(OperationsConstants.Reindex, StringComparison.OrdinalIgnoreCase)
                && _initialized)
            {
                if (!_intervalStart.HasValue)
                {
                    _intervalStart = Clock.UtcNow;
                }

                // we want to sum all the consumed RUs over a period of time
                // that is about 5x the current delay between queries
                // then we average that RU consumption per second
                // and compare it against the target
                if ((Clock.UtcNow - _intervalStart).Value.TotalMilliseconds <
                    (5 * (ReindexJobRecord.QueryDelayIntervalInMilliseconds + GetThrottleBasedDelay())))
                {
                    _rUsConsumedDuringInterval += notification.TotalRequestCharge;
                }
                else
                {
                    // calculate average RU consumption per second
                    _rUsConsumedDuringInterval += notification.TotalRequestCharge;

                    double averageRUsConsumed = _rUsConsumedDuringInterval / (Clock.UtcNow - _intervalStart).Value.TotalSeconds;

                    if (averageRUsConsumed > _targetRUs)
                    {
                        _delayFactor += 1;
                    }
                    else if (averageRUsConsumed < (_targetRUs * 0.75)
                        && _delayFactor > 0)
                    {
                        _delayFactor -= 1;
                    }

                    _intervalStart = Clock.UtcNow;
                    _rUsConsumedDuringInterval = 0.0;
                }
            }

            return Task.CompletedTask;
        }
    }
}
