// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Api.Features.ApiNotifications;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.CosmosDb.Features.Metrics;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Fhir.Tests.E2E.Rest.Audit;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Metric
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    [HttpIntegrationFixtureArgumentSets(DataStore.CosmosDb, Format.Json)]
    public class MetricTests : IClassFixture<MetricTestFixture>, IAsyncLifetime
    {
        private readonly MetricTestFixture _fixture;
        private readonly TestFhirClient _client;

        private readonly MetricHandler _metricHandler;

        public MetricTests(MetricTestFixture fixture)
        {
            _fixture = fixture;
            _client = fixture.TestFhirClient;
            _metricHandler = _fixture?.MetricHandler;
        }

        public async Task InitializeAsync()
        {
            // Send an empty request to guarantee that there is a bearer token set and the call isn't recorded in the metric handler.
            await _client.HttpClient.SendAsync(new HttpRequestMessage(HttpMethod.Get, string.Empty));
        }

        public Task DisposeAsync()
        {
            return Task.CompletedTask;
        }

        [Fact]
        public async Task GivenAResource_WhenCreated_ThenCorrectNumberOfMetricNotificationsShouldBeEmitted()
        {
            _metricHandler?.ResetCount();

            await ExecuteAndValidate(
                async () =>
                {
                    var result = await _client.CreateAsync(Samples.GetDefaultObservation().ToPoco());
                    return result.Response;
                },
                (type: typeof(ApiResponseNotification), count: 1, resourceType: Samples.GetDefaultObservation().ToPoco().TypeName),
                (type: typeof(CosmosStorageRequestMetricsNotification), count: 1, resourceType: Samples.GetDefaultObservation().ToPoco().TypeName));
        }

        [Fact]
        public async Task GivenHealthCheckPath_WhenInvoked_MetricNotificationsNotEmitted()
        {
            _metricHandler?.ResetCount();

            await ExecuteAndValidate(
                () => _client.HttpClient.GetAsync("/health/check"),
                (type: typeof(ApiResponseNotification), count: 0, resourceType: (string)null),
                (type: typeof(CosmosStorageRequestMetricsNotification), count: 2, resourceType: (string)null));
        }

        [Trait(Traits.Category, Categories.Bundle)]
        [Trait(Traits.Priority, Priority.One)]
        [Fact]
        public async Task GivenABatch_WhenInvokedAtCosmosDb_MetricNotificationsShouldBeEmitted()
        {
            _metricHandler?.ResetCount();
            var requestBundle = Samples.GetDefaultBatch().ToPoco<Hl7.Fhir.Model.Bundle>();

            await ExecuteAndValidate(
                 async () =>
                 {
                     var result = await _client.PostBundleAsync(Samples.GetDefaultBatch().ToPoco());
                     return result.Response;
                 },
                 (type: typeof(ApiResponseNotification), count: 1, resourceType: (string)null),
                 (type: typeof(CosmosStorageRequestMetricsNotification), count: 11, resourceType: "Patient"));
        }

        [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer, Format.Json)]
        [Trait(Traits.Category, Categories.Bundle)]
        [Trait(Traits.Priority, Priority.One)]
        [Fact]
        public async Task GivenABatch_WhenInvokedAtSqlServer_MetricNotificationsShouldBeEmitted()
        {
            _metricHandler?.ResetCount();

            await ExecuteAndValidate(
                async () =>
                {
                    var result = await _client.PostBundleAsync(Samples.GetDefaultBatch().ToPoco());
                    return result.Response;
                },
                (type: typeof(ApiResponseNotification), count: 1, resourceType: (string)null));
        }

        [HttpIntegrationFixtureArgumentSets(dataStores: DataStore.SqlServer)]
        [Trait(Traits.Category, Categories.Transaction)]
        [Trait(Traits.Priority, Priority.One)]
        [Fact]
        public async Task GivenATransaction_WhenInvoked_MetricNotificationsShouldBeEmitted()
        {
            _metricHandler?.ResetCount();

            var requestBundle = Samples.GetJsonSample("Bundle-TransactionWithValidBundleEntry").ToPoco<Hl7.Fhir.Model.Bundle>();

            await ExecuteAndValidate(
                async () =>
                {
                    var result = await _client.PostBundleAsync(requestBundle);
                    return result.Response;
                },
                (type: typeof(ApiResponseNotification), count: 1, resourceType: (string)null));
        }

        private async Task ExecuteAndValidate<T>(Func<Task<T>> action, params (Type type, int count, string resourceType)[] expectedNotifications)
        {
            if (!_fixture.IsUsingInProcTestServer)
            {
                // This test only works with the in-proc server with a customized metric handler.
                return;
            }

            var result = await action() as HttpResponseMessage;

            foreach ((Type type, int count, string resourceType) expectedNotification in expectedNotifications)
            {
                if (expectedNotification.count == 0)
                {
                    Assert.False(_metricHandler.NotificationMapping.TryGetValue(expectedNotification.type, out var _));
                    continue;
                }

                Assert.Equal(expectedNotification.count, _metricHandler.NotificationMapping[expectedNotification.type].Count);

                if (result != null && expectedNotification.type == typeof(CosmosStorageRequestMetricsNotification))
                {
                    result.Headers.TryGetValues(CosmosDbHeaders.RequestCharge, out IEnumerable<string> values);

                    Assert.NotNull(values);

                    foreach (var notification in _metricHandler.NotificationMapping[expectedNotification.type])
                    {
                        var casted = notification as CosmosStorageRequestMetricsNotification;
                        Assert.Equal(expectedNotification.resourceType, casted.ResourceType);
                    }
                }

                if (result != null && expectedNotification.type == typeof(ApiResponseNotification))
                {
                    foreach (var notification in _metricHandler.NotificationMapping[expectedNotification.type])
                    {
                        var casted = notification as ApiResponseNotification;
                        Assert.Equal(expectedNotification.resourceType, casted.ResourceType);
                    }
                }
            }
        }
    }
}
