// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Health.Fhir.Core.Extensions;
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
            _metricHandler = _fixture.MetricHandler;
        }

        [Fact]
        public async Task GivenAResource_WhenCreated_ThenCorrectNumberOfMetricNotificationsShouldBeEmitted()
        {
            await ExecuteAndValidate(
                () => _client.CreateAsync(Samples.GetDefaultObservation().ToPoco()),
                2);

            _metricHandler.ResetCount();
        }

        [Fact]
        public async Task GivenHealthCheckPath_WhenInvoked_MetricNotificationsNotEmitted()
        {
            await ExecuteAndValidate(
                () => _client.HttpClient.GetAsync(FhirServerApplicationBuilderExtensions.HealthCheckPath),
                0);

            _metricHandler.ResetCount();
        }

        private async Task ExecuteAndValidate<T>(Func<Task<T>> action, int expectedNotifications)
        {
            if (!_fixture.IsUsingInProcTestServer)
            {
                // This test only works with the in-proc server with a customized metric handler.
                return;
            }

            await action();

            Assert.Equal(expectedNotifications, _metricHandler.HandleCount);
        }
    }
}
