// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Fhir.Tests.E2E.Crucible.Client.Models;
using Newtonsoft.Json;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Crucible.Client
{
    public class CrucibleClient
    {
        private readonly HttpClient _client;
        private string _serverBase;
        private const int TimoutInMinutes = 30;
        private const int PollingDelayMs = 30000;
        private static SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

        public CrucibleClient()
        {
            _client = new HttpClient();
        }

        public string ServerBase => _serverBase;

        public async Task SetTestServerAsync(string crucibleUrl, string fhirServerUrl, string fhirServerName)
        {
            EnsureArg.IsNotNullOrEmpty(crucibleUrl, nameof(crucibleUrl));
            EnsureArg.IsNotNullOrEmpty(fhirServerUrl, nameof(fhirServerUrl));

            using var message = new HttpRequestMessage(HttpMethod.Post, $"{crucibleUrl}/servers")
            {
                Content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("server[url]", fhirServerUrl),
                    new KeyValuePair<string, string>("server[name]", fhirServerName),
                }),
            };

            using HttpResponseMessage response = await _client.SendAsync(message);

            _serverBase = response.RequestMessage.RequestUri.ToString();
        }

        public async Task RefreshConformanceStatementAsync()
        {
            using var message = new HttpRequestMessage(HttpMethod.Get, $"{_serverBase}/conformance?refresh=true");

            await _client.SendAsync(message);
        }

        public async Task AuthorizeServerAsync(Uri authorizeUri, Uri tokenUri)
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, $"{_serverBase}/oauth_authorize_backend")
            {
                Content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("oauth_type", "client_credentials"),
                    new KeyValuePair<string, string>("client_id", TestApplications.GlobalAdminServicePrincipal.ClientId),
                    new KeyValuePair<string, string>("client_secret", TestApplications.GlobalAdminServicePrincipal.ClientSecret),
                    new KeyValuePair<string, string>("authorize_url", authorizeUri.ToString()),
                    new KeyValuePair<string, string>("token_url", tokenUri.ToString()),
                    new KeyValuePair<string, string>("endpoint_params", $"resource={AuthenticationSettings.Resource}&scope={AuthenticationSettings.Scope}"),
                }),
            };

            await _client.SendAsync(message);
        }

        public async Task<Test[]> GetSupportedTestsAsync()
        {
            using var message = new HttpRequestMessage(HttpMethod.Get, $"{_serverBase}/supported_tests.json");

            using HttpResponseMessage response = await _client.SendAsync(message);

            return JsonConvert.DeserializeObject<SupportedTests>(await response.Content.ReadAsStringAsync()).Tests;
        }

        public async Task<PastTestRunResponse> PastTestRunsAsync()
        {
            using var message = new HttpRequestMessage(HttpMethod.Get, $"{_serverBase}/past_runs");

            using HttpResponseMessage response = await _client.SendAsync(message);

            return JsonConvert.DeserializeObject<PastTestRunResponse>(await response.Content.ReadAsStringAsync());
        }

        public async Task<TestRun> BeginTestRunAsync(IEnumerable<string> testIds, bool onlySupported)
        {
            EnsureArg.IsNotNull(testIds, nameof(testIds));

            using var message = new HttpRequestMessage(HttpMethod.Post, $"{_serverBase}/test_runs.json")
            {
                Content = new FormUrlEncodedContent(
                    testIds.Select(x => new KeyValuePair<string, string>("test_ids[]", x))
                        .Concat(new[] { new KeyValuePair<string, string>("supported_only", onlySupported.ToString().ToLowerInvariant()) })),
            };

            using HttpResponseMessage response = await _client.SendAsync(message);

            return JsonConvert.DeserializeObject<TestRunResponse>(await response.Content.ReadAsStringAsync()).TestRun;
        }

        public async Task<TestRun> GetTestRunStatusAsync(string testRunId)
        {
            EnsureArg.IsNotNullOrEmpty(testRunId, nameof(testRunId));

            using var message = new HttpRequestMessage(HttpMethod.Get, $"{_serverBase}/test_runs/{testRunId}");

            using HttpResponseMessage response = await _client.SendAsync(message);

            var json = await response.Content.ReadAsStringAsync();

            return JsonConvert.DeserializeObject<TestRunResponse>(json).TestRun;
        }

        public async Task<TestRun> RunTestsAndWaitAsync(IEnumerable<string> testIds, bool onlySupported)
        {
            EnsureArg.IsNotNull(testIds, nameof(testIds));

            await _semaphore.WaitAsync();
            try
            {
                var maxTime = DateTimeOffset.UtcNow.AddMinutes(TimoutInMinutes);
                var response = await BeginTestRunAsync(testIds, onlySupported);
                var id = response.Id;

                do
                {
                    response = await GetTestRunStatusAsync(id);
                    await Task.Delay(PollingDelayMs);

                    if (DateTimeOffset.UtcNow > maxTime)
                    {
                        throw new TimeoutException("Timed out waiting for Crucible tests to complete");
                    }
                }
                while (response.Status != "finished" && response.Status != "cancelled");

                return response;
            }
            finally
            {
                _semaphore.Release();
            }
        }
    }
}
