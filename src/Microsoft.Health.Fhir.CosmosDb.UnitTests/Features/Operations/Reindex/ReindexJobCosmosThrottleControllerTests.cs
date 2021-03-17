// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex.Models;
using Microsoft.Health.Fhir.CosmosDb.Features.Metrics;
using Microsoft.Health.Fhir.CosmosDb.Features.Operations.Reindex;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Health.Fhir.CosmosDb.UnitTests.Features.Operations.Reindex
{
    public class ReindexJobCosmosThrottleControllerTests
    {
        private ITestOutputHelper _output;

        public ReindexJobCosmosThrottleControllerTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task GivenATargetRUConsumption_WhenConsumedRUsIsTooHigh_QueryDelayIsIncreased()
        {
            var throttleController = new ReindexJobCosmosThrottleController(1000);
            var reindexJob = new ReindexJobRecord(new Dictionary<string, string>(), targetDataStoreResourcePercentage: 80);
            reindexJob.QueryDelayIntervalInMilliseconds = 50;
            throttleController.Initialize(reindexJob);

            var cosmosMetrics = new CosmosStorageRequestMetricsNotification(OperationsConstants.Reindex, "Resource");
            cosmosMetrics.TotalRequestCharge = 100;
            int loopCount = 0;

            while (loopCount < 17)
            {
                _output.WriteLine($"Current throttle based delay is: {throttleController.GetThrottleBasedDelay()}");
                await throttleController.Handle(cosmosMetrics, CancellationToken.None);
                await Task.Delay(reindexJob.QueryDelayIntervalInMilliseconds + throttleController.GetThrottleBasedDelay());
                loopCount++;
            }

            _output.WriteLine($"Final throttle based delay is: {throttleController.GetThrottleBasedDelay()}");
            Assert.Equal(100, throttleController.GetThrottleBasedDelay());
        }

        [Fact]
        public async Task GivenATargetRUConsumption_WhenConsumedRUsDecreases_QueryDelayIsDecreased()
        {
            var throttleController = new ReindexJobCosmosThrottleController(1000);
            var reindexJob = new ReindexJobRecord(new Dictionary<string, string>(), targetDataStoreResourcePercentage: 80);
            reindexJob.QueryDelayIntervalInMilliseconds = 50;
            throttleController.Initialize(reindexJob);

            var cosmosMetrics = new CosmosStorageRequestMetricsNotification(OperationsConstants.Reindex, "Resource");
            cosmosMetrics.TotalRequestCharge = 100;
            int loopCount = 0;

            while (loopCount < 17)
            {
                _output.WriteLine($"Current throttle based delay is: {throttleController.GetThrottleBasedDelay()}");
                await throttleController.Handle(cosmosMetrics, CancellationToken.None);
                await Task.Delay(reindexJob.QueryDelayIntervalInMilliseconds + throttleController.GetThrottleBasedDelay());
                loopCount++;
            }

            cosmosMetrics.TotalRequestCharge = 10;
            loopCount = 0;

            while (loopCount < 17)
            {
                _output.WriteLine($"Current throttle based delay is: {throttleController.GetThrottleBasedDelay()}");
                await throttleController.Handle(cosmosMetrics, CancellationToken.None);
                await Task.Delay(reindexJob.QueryDelayIntervalInMilliseconds + throttleController.GetThrottleBasedDelay());
                loopCount++;
            }

            _output.WriteLine($"Final throttle based delay is: {throttleController.GetThrottleBasedDelay()}");
            Assert.Equal(0, throttleController.GetThrottleBasedDelay());
        }

        [Fact]
        public void GivenThrottleControllerNotInitialized_WhenGetThrottleDelayCalled_ZeroReturned()
        {
            var throttleController = new ReindexJobCosmosThrottleController(null);
            Assert.Equal(0, throttleController.GetThrottleBasedDelay());

            throttleController = new ReindexJobCosmosThrottleController(1000);
            Assert.Equal(0, throttleController.GetThrottleBasedDelay());

            var reindexJob = new ReindexJobRecord(new Dictionary<string, string>(), targetDataStoreResourcePercentage: null);
            reindexJob.QueryDelayIntervalInMilliseconds = 50;
            throttleController.Initialize(reindexJob);
            Assert.Equal(0, throttleController.GetThrottleBasedDelay());
        }
    }
}
