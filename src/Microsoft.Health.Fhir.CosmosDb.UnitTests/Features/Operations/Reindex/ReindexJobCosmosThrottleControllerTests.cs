// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex.Models;
using Microsoft.Health.Fhir.CosmosDb.Features.Operations.Reindex;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Health.Fhir.CosmosDb.UnitTests.Features.Operations.Reindex
{
    [CollectionDefinition("ReindexThrottle", DisableParallelization = true)]
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.IndexAndReindex)]
    public class ReindexJobCosmosThrottleControllerTests
    {
        private ITestOutputHelper _output;
        private readonly RequestContextAccessor<IFhirRequestContext> _fhirRequestContextAccessor;

        public ReindexJobCosmosThrottleControllerTests(ITestOutputHelper output)
        {
            _output = output;
            _fhirRequestContextAccessor = new FhirRequestContextAccessor();
            var fhirRequestContext = new FhirRequestContext(
                method: OperationsConstants.Reindex,
                uriString: "$reindex",
                baseUriString: "$reindex",
                correlationId: "id",
                requestHeaders: new Dictionary<string, StringValues>(),
                responseHeaders: new Dictionary<string, StringValues>())
            {
                IsBackgroundTask = true,
                AuditEventType = OperationsConstants.Reindex,
            };
            _fhirRequestContextAccessor.RequestContext = fhirRequestContext;
        }

        [Fact]
        public async Task GivenATargetRUConsumption_WhenConsumedRUsIsTooHigh_QueryDelayIsIncreased()
        {
            var throttleController = new ReindexJobCosmosThrottleController(_fhirRequestContextAccessor, new NullLogger<ReindexJobCosmosThrottleController>());
            var reindexJob = new ReindexJobRecord(new Dictionary<string, string>(), new List<string>(), targetDataStoreUsagePercentage: 80);
            reindexJob.QueryDelayIntervalInMilliseconds = 50;
            throttleController.Initialize(reindexJob, 1000);

            int loopCount = 0;

            while (loopCount < 25)
            {
                _output.WriteLine($"Current throttle based delay is: {throttleController.GetThrottleBasedDelay()}");
                _fhirRequestContextAccessor.RequestContext.ResponseHeaders.Add(CosmosDbHeaders.RequestCharge, "100.0");
                throttleController.UpdateDatastoreUsage();
                await Task.Delay(reindexJob.QueryDelayIntervalInMilliseconds + throttleController.GetThrottleBasedDelay());
                loopCount++;
            }

            _output.WriteLine($"Final throttle based delay is: {throttleController.GetThrottleBasedDelay()}");
            Assert.True(throttleController.GetThrottleBasedDelay() > 0, "Asserting that the value is > 0");
        }

        [Fact]
        public async Task GivenATargetRUConsumption_WhenConsumedRUsDecreases_QueryDelayIsDecreased()
        {
            var throttleController = new ReindexJobCosmosThrottleController(_fhirRequestContextAccessor, new NullLogger<ReindexJobCosmosThrottleController>());
            var reindexJob = new ReindexJobRecord(new Dictionary<string, string>(), new List<string>(), targetDataStoreUsagePercentage: 80);
            reindexJob.QueryDelayIntervalInMilliseconds = 50;
            throttleController.Initialize(reindexJob, 1000);

            int loopCount = 0;

            while (loopCount < 17)
            {
                _output.WriteLine($"Current throttle based delay is: {throttleController.GetThrottleBasedDelay()}");
                _fhirRequestContextAccessor.RequestContext.ResponseHeaders.Add(CosmosDbHeaders.RequestCharge, "100.0");
                throttleController.UpdateDatastoreUsage();
                await Task.Delay(reindexJob.QueryDelayIntervalInMilliseconds + throttleController.GetThrottleBasedDelay());
                loopCount++;
            }

            loopCount = 0;

            while (loopCount < 17)
            {
                _output.WriteLine($"Current throttle based delay is: {throttleController.GetThrottleBasedDelay()}");
                _fhirRequestContextAccessor.RequestContext.ResponseHeaders.Add(CosmosDbHeaders.RequestCharge, "10.0");
                throttleController.UpdateDatastoreUsage();
                await Task.Delay(reindexJob.QueryDelayIntervalInMilliseconds + throttleController.GetThrottleBasedDelay());
                loopCount++;
            }

            _output.WriteLine($"Final throttle based delay is: {throttleController.GetThrottleBasedDelay()}");
            Assert.Equal(0, throttleController.GetThrottleBasedDelay());
        }

        [Fact]
        public void GivenThrottleControllerNotInitialized_WhenGetThrottleDelayCalled_ZeroReturned()
        {
            var throttleController = new ReindexJobCosmosThrottleController(_fhirRequestContextAccessor, new NullLogger<ReindexJobCosmosThrottleController>());
            Assert.Equal(0, throttleController.GetThrottleBasedDelay());

            var reindexJob = new ReindexJobRecord(new Dictionary<string, string>(), new List<string>(), targetDataStoreUsagePercentage: null);
            reindexJob.QueryDelayIntervalInMilliseconds = 50;
            throttleController.Initialize(reindexJob, null);
            Assert.Equal(0, throttleController.GetThrottleBasedDelay());
        }

        [Fact]
        public void GivenBatchSizeCostAboveTarget_WhenGetBatchSizeCalled_ReducedBatchSizeReturned()
        {
            var throttleController = new ReindexJobCosmosThrottleController(_fhirRequestContextAccessor, new NullLogger<ReindexJobCosmosThrottleController>());
            var reindexJob = new ReindexJobRecord(new Dictionary<string, string>(), new List<string>(), targetDataStoreUsagePercentage: 80);
            reindexJob.QueryDelayIntervalInMilliseconds = 50;
            throttleController.Initialize(reindexJob, 1000);

            _fhirRequestContextAccessor.RequestContext.ResponseHeaders.Add(CosmosDbHeaders.RequestCharge, "1000.0");
            throttleController.UpdateDatastoreUsage();

            Assert.Equal<uint>(80, throttleController.GetThrottleBatchSize());

            _fhirRequestContextAccessor.RequestContext.ResponseHeaders.Add(CosmosDbHeaders.RequestCharge, "500.0");
            throttleController.UpdateDatastoreUsage();

            Assert.Equal<uint>(100, throttleController.GetThrottleBatchSize());
        }
    }
}
