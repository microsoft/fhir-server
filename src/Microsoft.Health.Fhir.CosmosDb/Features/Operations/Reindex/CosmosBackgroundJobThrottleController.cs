// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Core;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex.Models;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Operations.Reindex
{
    public class CosmosBackgroundJobThrottleController : IBackgroundJobThrottleController
    {
        private int? _provisionedRUThroughput;
        private DateTimeOffset? _intervalStart = null;
        private double _rUsConsumedDuringInterval = 0.0;
        private ushort _delayFactor = 0;
        private double? _targetRUs = null;
        private uint _targetBatchSize = 100;
        private uint _jobConfiguredBatchSize = 100;
        private bool _initialized = false;
        private readonly RequestContextAccessor<IFhirRequestContext> _fhirRequestContextAccessor;
        private readonly Func<IScoped<IFhirDataStore>> _fhirDataStoreFactory;
        private readonly ILogger<CosmosBackgroundJobThrottleController> _logger;

        public CosmosBackgroundJobThrottleController(
            RequestContextAccessor<IFhirRequestContext> fhirRequestContextAccessor,
            Func<IScoped<IFhirDataStore>> fhirDataStoreFactory,
            ILogger<CosmosBackgroundJobThrottleController> logger)
        {
            _fhirRequestContextAccessor = EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));
            _fhirDataStoreFactory = EnsureArg.IsNotNull(fhirDataStoreFactory, nameof(fhirDataStoreFactory));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
        }

        public IThrottleableJobRecord JobRecord { get; set; } = null;

        public async Task Initialize(IThrottleableJobRecord jobRecord, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(jobRecord, nameof(jobRecord));

            if (jobRecord.TargetDataStoreUsagePercentage != null &&
                    jobRecord.TargetDataStoreUsagePercentage > 0)
            {
                using (IScoped<IFhirDataStore> store = _fhirDataStoreFactory.Invoke())
                {
                    _provisionedRUThroughput = await store.Value.GetProvisionedDataStoreCapacityAsync(cancellationToken);
                }
            }
            else
            {
                _provisionedRUThroughput = null;
            }

            JobRecord = jobRecord;
            _jobConfiguredBatchSize = jobRecord.MaximumNumberOfResourcesPerQuery;

            if (JobRecord.TargetDataStoreUsagePercentage.HasValue
                && JobRecord.TargetDataStoreUsagePercentage.Value > 0
                && _provisionedRUThroughput.HasValue
                && _provisionedRUThroughput > 0)
            {
                _targetRUs = _provisionedRUThroughput.Value * (JobRecord.TargetDataStoreUsagePercentage.Value / 100.0);
                _logger.LogInformation("Background job throttling initialized, target RUs: {TargetRUs}", _targetRUs);
                _delayFactor = 0;
                _rUsConsumedDuringInterval = 0.0;
                _initialized = true;
                _targetBatchSize = jobRecord.MaximumNumberOfResourcesPerQuery;
            }
            else
            {
                _logger.LogInformation("Unable to initialize throttle controller.  Throttling unavailable. Provisioned RUs: {ProvisionRU}", _provisionedRUThroughput);
            }
        }

        public int GetThrottleBasedDelay()
        {
            if (!_initialized)
            {
                // not initialized
                return 0;
            }

            return JobRecord.QueryDelayIntervalInMilliseconds * _delayFactor;
        }

        public uint GetThrottleBatchSize()
        {
            if (!_initialized)
            {
                // not initialized
                return _jobConfiguredBatchSize;
            }

            return _targetBatchSize;
        }

        /// <summary>
        /// Captures the currently recorded database consumption
        /// </summary>
        /// <returns>Returns an average database resource consumtion per second</returns>
        public double UpdateDatastoreUsage()
        {
            double averageRUsConsumed = 0.0;

            if (_initialized)
            {
                var requestContext = _fhirRequestContextAccessor.RequestContext;

                // Debug.Assert(
                //    requestContext.Method.Equals(OperationsConstants.Reindex, StringComparison.OrdinalIgnoreCase),
                //    "We should not be here with FhirRequestContext that is not reindex!");

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

                if (responseRequestCharge > _targetRUs)
                {
                    double batchPercent = _targetRUs.Value / responseRequestCharge;
                    _targetBatchSize = (uint)(_targetBatchSize * batchPercent);
                    _targetBatchSize = _targetBatchSize >= 10 ? _targetBatchSize : 10;
                    _logger.LogInformation("Query for one batch was larger than target RUs, current query cost: {ResponseRequestCharge}.  Reduced batch size to: {TargetBatchSize}", responseRequestCharge, _targetBatchSize);
                }
                else
                {
                    _targetBatchSize = _jobConfiguredBatchSize;
                }

                _rUsConsumedDuringInterval += responseRequestCharge;

                // calculate average RU consumption per second
                averageRUsConsumed = _rUsConsumedDuringInterval / (Clock.UtcNow - _intervalStart).Value.TotalSeconds;

                // we want to sum all the consumed RUs over a period of time
                // that is about 5x the current delay between queries
                // then we average that RU consumption per second
                // and compare it against the target
                if ((Clock.UtcNow - _intervalStart).Value.TotalMilliseconds >=
                    (5 * (JobRecord.QueryDelayIntervalInMilliseconds + GetThrottleBasedDelay())))
                {
                    if (averageRUsConsumed > _targetRUs)
                    {
                        _delayFactor += 1;
                        _logger.LogInformation("Background Job RU consumption high, delay factor increase to: {DelayFactor}", _delayFactor);
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

            return averageRUsConsumed;
        }
    }
}
