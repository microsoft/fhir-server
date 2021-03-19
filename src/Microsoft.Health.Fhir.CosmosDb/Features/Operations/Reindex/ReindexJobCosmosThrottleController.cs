// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Diagnostics;
using EnsureThat;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Core;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex.Models;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Operations.Reindex
{
    public class ReindexJobCosmosThrottleController : IReindexJobThrottleController
    {
        private int? _provisionedRUThroughput;
        private DateTimeOffset? _intervalStart = null;
        private double _rUsConsumedDuringInterval = 0.0;
        private ushort _delayFactor = 0;
        private double? _targetRUs = null;
        private bool _initialized = false;
        private readonly IFhirRequestContextAccessor _fhirRequestContextAccessor;

        public ReindexJobCosmosThrottleController(IFhirRequestContextAccessor fhirRequestContextAccessor)
        {
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));

            _fhirRequestContextAccessor = fhirRequestContextAccessor;
        }

        public ReindexJobRecord ReindexJobRecord { get; set; } = null;

        public void Initialize(ReindexJobRecord reindexJobRecord, int? provisionedDatastoreCapacity)
        {
            EnsureArg.IsNotNull(reindexJobRecord, nameof(reindexJobRecord));

            ReindexJobRecord = reindexJobRecord;
            _provisionedRUThroughput = provisionedDatastoreCapacity;

            if (ReindexJobRecord.TargetDataStoreResourcePercentage.HasValue
                && ReindexJobRecord.TargetDataStoreResourcePercentage.Value > 0
                && _provisionedRUThroughput.HasValue
                && _provisionedRUThroughput > 0)
            {
                _targetRUs = _provisionedRUThroughput.Value * (ReindexJobRecord.TargetDataStoreResourcePercentage.Value / 100.0);
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

        public void UpdateDatastoreUsage()
        {
            if (_initialized)
            {
                var requestContext = _fhirRequestContextAccessor.FhirRequestContext;
                Debug.Assert(
                    !requestContext.Method.Equals(OperationsConstants.Reindex, StringComparison.OrdinalIgnoreCase),
                    "We should not be here with FhirRequestContext that is not reindex!");

                if (!_intervalStart.HasValue)
                {
                    _intervalStart = Clock.UtcNow;
                }

                double responseRequestCharge = 0.0;

                if (requestContext.ResponseHeaders.TryGetValue(CosmosDbHeaders.RequestCharge, out StringValues existingHeaderValue))
                {
                    if (double.TryParse(existingHeaderValue.ToString(), out double headerRequestCharge))
                    {
                        responseRequestCharge += headerRequestCharge;
                    }
                }

                // we want to sum all the consumed RUs over a period of time
                // that is about 5x the current delay between queries
                // then we average that RU consumption per second
                // and compare it against the target
                if ((Clock.UtcNow - _intervalStart).Value.TotalMilliseconds <
                    (5 * (ReindexJobRecord.QueryDelayIntervalInMilliseconds + GetThrottleBasedDelay())))
                {
                    _rUsConsumedDuringInterval += responseRequestCharge;
                }
                else
                {
                    // calculate average RU consumption per second
                    _rUsConsumedDuringInterval += responseRequestCharge;

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

                // clear out the value in the FhirRequestContext
                requestContext.ResponseHeaders.Remove(CosmosDbHeaders.RequestCharge);
            }
        }
    }
}
