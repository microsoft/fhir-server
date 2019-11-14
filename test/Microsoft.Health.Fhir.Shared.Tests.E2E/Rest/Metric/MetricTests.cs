// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Health.CosmosDb.Features.Storage;
using Microsoft.Health.Fhir.Api.Features.ApiNotifications;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.CosmosDb.Features.Metrics;
using Microsoft.Health.Fhir.Shared.Tests.E2E.Rest.Metric;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Fhir.Tests.E2E.Rest.Audit;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Metric
{
    [HttpIntegrationFixtureArgumentSets(DataStore.CosmosDb, Format.Json)]
    public class MetricTests : IClassFixture<MetricTestFixture>
    {
        private readonly MetricTestFixture _fixture;
        private readonly FhirClient _client;

        private readonly MetricHandler _metricHandler;

        public MetricTests(MetricTestFixture fixture)
        {
            _fixture = fixture;
            _client = fixture.FhirClient;
            _metricHandler = _fixture?.MetricHandler;
        }

        [Fact]
        public async Task GivenAResource_WhenCreated_ThenCorrectNumberOfMetricNotificationsShouldBeEmitted()
        {
            _metricHandler?.ResetCount();

            await ExecuteAndValidate(
                () => _client.CreateAsync(Samples.GetDefaultObservation().ToPoco()),
                (type: typeof(ApiResponseNotification), count: 1),
                (type: typeof(CosmosStorageRequestMetricsNotification), count: 1));
        }

        [Fact]
        public async Task GivenHealthCheckPath_WhenInvoked_MetricNotificationsNotEmitted()
        {
            _metricHandler?.ResetCount();

            await ExecuteAndValidate(
                () => _client.HttpClient.GetAsync(FhirServerApplicationBuilderExtensions.HealthCheckPath),
                (type: typeof(ApiResponseNotification), count: 0),
                (type: typeof(CosmosStorageRequestMetricsNotification), count: 2));
        }

        private async Task ExecuteAndValidate<T>(Func<Task<T>> action, params (Type type, int count)[] expectedNotifications)
        {
            if (!_fixture.IsUsingInProcTestServer)
            {
                // This test only works with the in-proc server with a customized metric handler.
                return;
            }

            var result = await action() as HttpResponseMessage;

            foreach ((Type type, int count) expectedNotification in expectedNotifications)
            {
                if (expectedNotification.count == 0)
                {
                    Assert.False(_metricHandler.HandleCountDictionary.TryGetValue(expectedNotification.type, out var count));
                    continue;
                }

                Assert.Equal(expectedNotification.count, _metricHandler.HandleCountDictionary[expectedNotification.type]);

                if (result != null && expectedNotification.type == typeof(CosmosStorageRequestMetricsNotification))
                {
                    result.Headers.TryGetValues(CosmosDbHeaders.RequestCharge, out IEnumerable<string> values);

                    Assert.NotNull(values);
                    Assert.Equal(expectedNotification.count, values.Count());
                }
            }
        }
    }
}
