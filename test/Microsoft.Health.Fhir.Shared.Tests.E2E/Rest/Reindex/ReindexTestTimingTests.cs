// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Core.Features.Operations.SearchParameterState;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;
using FhirClient = Microsoft.Health.Fhir.Client.FhirClient;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Reindex
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.IndexAndReindex)]
    public class ReindexTestTimingTests
    {
        private const string OldUrl = "http://my.org/old";
        private const string NewUrl = "http://my.org/new";
        private static readonly Uri JobUri = new Uri("http://localhost/$reindex/1");
        private readonly ITestOutputHelper _output;

        public ReindexTestTimingTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void GivenNoRemoteInterval_WhenResolving_ThenUsesServerDefault()
        {
            var interval = RemoteTestFhirServer.ParseSearchParameterCacheRefreshInterval(null);

            Assert.Equal(TimeSpan.FromSeconds(20), interval);
        }

        [Theory]
        [InlineData("1", 1)]
        [InlineData("20", 20)]
        [InlineData("60", 60)]
        [InlineData("429496", 429496)]
        public void GivenRemoteInterval_WhenResolving_ThenUsesConfiguredSeconds(string value, int seconds)
        {
            var interval = RemoteTestFhirServer.ParseSearchParameterCacheRefreshInterval(value);

            Assert.Equal(TimeSpan.FromSeconds(seconds), interval);
        }

        [Theory]
        [InlineData("0")]
        [InlineData("-1")]
        [InlineData("1.5")]
        [InlineData("invalid")]
        [InlineData("2147483648")]
        [InlineData("429497")]
        public void GivenInvalidRemoteInterval_WhenResolving_ThenReportsSettingName(string value)
        {
            var error = Assert.Throws<InvalidOperationException>(() => RemoteTestFhirServer.ParseSearchParameterCacheRefreshInterval(value));

            Assert.Contains(KnownEnvironmentVariableNames.TestSearchParameterCacheRefreshIntervalSeconds, error.Message);
        }

        [Fact]
        public async Task GivenDelayedUrlTransition_WhenPolling_ThenRequiresBothExpectedStates()
        {
            var cycle = 0;
            using var http = CreateHttpClient((request, _) =>
            {
                var isOldUrl = Uri.UnescapeDataString(request.RequestUri.Query).Contains(OldUrl, StringComparison.Ordinal);
                if (isOldUrl)
                {
                    cycle++;
                    return Task.FromResult(Response(cycle == 1 || cycle == 3 ? Status(OldUrl, SearchParameterStatus.Supported) : new Parameters()));
                }

                return Task.FromResult(Response(cycle < 3 ? new Parameters() : Status(NewUrl, SearchParameterStatus.Supported)));
            });

            var responses = await ReindexTests.WaitForSearchParameterStatusesAsync(
                new FhirClient(http), [(OldUrl, null), (NewUrl, SearchParameterStatus.Supported)], TimeSpan.FromSeconds(2));

            Assert.Equal(4, cycle);
            Assert.Empty(responses[0].Parameter);
            Assert.Single(responses[1].Parameter);
        }

        [Fact]
        public async Task GivenConvergedStatuses_WhenPolling_ThenReturnsWithoutWaitingForTimeout()
        {
            var requests = 0;
            using var http = CreateHttpClient((request, _) =>
            {
                requests++;
                return Task.FromResult(Response(Uri.UnescapeDataString(request.RequestUri.Query).Contains(OldUrl, StringComparison.Ordinal)
                    ? new Parameters()
                    : Status(NewUrl, SearchParameterStatus.Supported)));
            });

            await ReindexTests.WaitForSearchParameterStatusesAsync(
                new FhirClient(http), [(OldUrl, null), (NewUrl, SearchParameterStatus.Supported)], TimeSpan.FromSeconds(20));

            Assert.Equal(2, requests);
        }

        [Fact]
        public async Task GivenWritePropagationWindow_WhenStatusesConvergeEarly_ThenWaitsForWindow()
        {
            var minimumWait = TimeSpan.FromMilliseconds(100);
            using var http = CreateHttpClient((_, _) => Task.FromResult(Response(Status(NewUrl, SearchParameterStatus.Supported))));
            var stopwatch = Stopwatch.StartNew();

            await ReindexTests.WaitForSearchParameterStatusesAsync(
                new FhirClient(http), [(NewUrl, SearchParameterStatus.Supported)], TimeSpan.FromSeconds(2), minimumWait);

            Assert.True(stopwatch.Elapsed >= minimumWait);
        }

        [Fact]
        public async Task GivenNonConvergingStatuses_WhenPollingTimesOut_ThenReportsBothLastResponses()
        {
            using var http = CreateHttpClient((request, _) => Task.FromResult(Response(Status(
                Uri.UnescapeDataString(request.RequestUri.Query).Contains(OldUrl, StringComparison.Ordinal) ? OldUrl : NewUrl,
                SearchParameterStatus.Enabled))));

            var error = await Assert.ThrowsAsync<TimeoutException>(() => ReindexTests.WaitForSearchParameterStatusesAsync(
                new FhirClient(http), [(OldUrl, null), (NewUrl, SearchParameterStatus.Supported)], TimeSpan.FromMilliseconds(500)));

            Assert.Contains(OldUrl, error.Message);
            Assert.Contains(NewUrl, error.Message);
            Assert.Contains("Enabled", error.Message);
            Assert.Contains("\"resourceType\":\"Parameters\"", error.Message);
        }

        [Fact]
        public async Task GivenBlockedStatusRequest_WhenPollingTimesOut_ThenCancelsRequest()
        {
            using var http = CreateHttpClient(async (_, token) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return Response(new Parameters());
            });

            await Assert.ThrowsAsync<TimeoutException>(() => ReindexTests.WaitForSearchParameterStatusesAsync(
                new FhirClient(http), [(NewUrl, SearchParameterStatus.Supported)], TimeSpan.FromMilliseconds(100)));
        }

        [Fact]
        public async Task GivenStatusRequestFailure_WhenPolling_ThenDoesNotSuppressError()
        {
            using var http = CreateHttpClient((_, _) => throw new HttpRequestException("status request failed"));

            var error = await Assert.ThrowsAsync<HttpRequestException>(() => ReindexTests.WaitForSearchParameterStatusesAsync(
                new FhirClient(http), [(NewUrl, SearchParameterStatus.Supported)], TimeSpan.FromSeconds(2)));

            Assert.Equal("status request failed", error.Message);
        }

        [Fact]
        public async Task GivenRunningJob_WhenUpdating_ThenRepeatsBeyondOnePassAndCountsOverlap()
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var statusRequests = 0;
            var puts = 0;
            using var http = CreateHttpClient(async (request, token) =>
            {
                if (request.Method == HttpMethod.Put)
                {
                    puts++;
                    return Response(new FhirJsonParser().Parse<Person>(await request.Content.ReadAsStringAsync(token)));
                }

                if (++statusRequests == 5)
                {
                    cancellation.Cancel();
                }

                return Response(new Parameters().Add("status", new FhirString("Running")));
            });

            var updates = await ReindexTests.UpdatePersonsDuringReindexAsync(
                new FhirClient(http), [("Person", "1")], JobUri, _output, cancellation.Token);

            Assert.Equal(2, puts);
            Assert.Equal(2, updates.Successful);
            Assert.Equal(2, updates.WhileRunning);
        }

        [Fact]
        public async Task GivenQueuedJob_WhenUpdating_ThenDoesNotCountQueuedWritesAsOverlap()
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var statusRequests = 0;
            using var http = CreateHttpClient((request, _) =>
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                if (++statusRequests == 2)
                {
                    cancellation.Cancel();
                }

                return Task.FromResult(Response(new Parameters().Add("status", new FhirString("Queued"))));
            });

            var updates = await ReindexTests.UpdatePersonsDuringReindexAsync(
                new FhirClient(http), [("Person", "1")], JobUri, _output, cancellation.Token);

            Assert.Equal(0, updates.Successful);
            Assert.Equal(0, updates.WhileRunning);
        }

        [Fact]
        public async Task GivenJobCompletesDuringPut_WhenUpdating_ThenDoesNotCountItAsOverlap()
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var updated = false;
            using var http = CreateHttpClient(async (request, token) =>
            {
                if (request.Method == HttpMethod.Put)
                {
                    updated = true;
                    return Response(new FhirJsonParser().Parse<Person>(await request.Content.ReadAsStringAsync(token)));
                }

                if (updated)
                {
                    cancellation.Cancel();
                }

                return Response(new Parameters().Add("status", new FhirString(updated ? "Completed" : "Running")));
            });

            var updates = await ReindexTests.UpdatePersonsDuringReindexAsync(
                new FhirClient(http), [("Person", "1")], JobUri, _output, cancellation.Token);

            Assert.Equal(1, updates.Successful);
            Assert.Equal(0, updates.WhileRunning);
        }

        [Fact]
        public async Task GivenUpdateFailure_WhenUpdating_ThenDoesNotSuppressError()
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var http = CreateHttpClient((request, _) => request.Method == HttpMethod.Put
                ? throw new HttpRequestException("PUT rejected")
                : Task.FromResult(Response(new Parameters().Add("status", new FhirString("Running")))));

            var error = await Assert.ThrowsAsync<HttpRequestException>(() => ReindexTests.UpdatePersonsDuringReindexAsync(
                new FhirClient(http), [("Person", "1")], JobUri, _output, cancellation.Token));

            Assert.Equal("PUT rejected", error.Message);
        }

        [Fact]
        public async Task GivenCanceledPut_WhenUpdating_ThenDoesNotCountItAsSuccessful()
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var putStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var http = CreateHttpClient(async (request, token) =>
            {
                if (request.Method == HttpMethod.Put)
                {
                    putStarted.SetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                }

                return Response(new Parameters().Add("status", new FhirString("Running")));
            });
            var updateTask = ReindexTests.UpdatePersonsDuringReindexAsync(
                new FhirClient(http), [("Person", "1")], JobUri, _output, cancellation.Token);
            await putStarted.Task.WaitAsync(cancellation.Token);

            await cancellation.CancelAsync();
            var updates = await updateTask;

            Assert.Equal(0, updates.Successful);
            Assert.Equal(0, updates.WhileRunning);
        }

        private static Parameters Status(string url, SearchParameterStatus status)
        {
            return new Parameters
            {
                Parameter =
                [
                    new Parameters.ParameterComponent
                    {
                        Name = SearchParameterStateProperties.Name,
                        Part =
                        [
                            new Parameters.ParameterComponent { Name = SearchParameterStateProperties.Url, Value = new FhirUri(url) },
                            new Parameters.ParameterComponent { Name = SearchParameterStateProperties.Status, Value = new FhirString(status.ToString()) },
                        ],
                    },
                ],
            };
        }

        private static HttpResponseMessage Response(Resource resource) =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(new FhirJsonSerializer().SerializeToString(resource)) };

        private static HttpClient CreateHttpClient(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) =>
            new HttpClient(new StubHttpMessageHandler(respond)) { BaseAddress = new Uri("http://localhost/") };

        private sealed class StubHttpMessageHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;

            public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
            {
                _respond = respond;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                _respond(request, cancellationToken);
        }
    }
}
