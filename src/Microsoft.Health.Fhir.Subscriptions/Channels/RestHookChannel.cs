// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Subscriptions.Models;
using Microsoft.Health.Fhir.Subscriptions.Persistence;

namespace Microsoft.Health.Fhir.Subscriptions.Channels
{
    [ChannelType(SubscriptionChannelType.RestHook)]
    #pragma warning disable CA1001 // Types that own disposable fields should be disposable
    public class RestHookChannel : IRestHookChannel
    #pragma warning restore CA1001 // Types that own disposable fields should be disposable
    {
        private readonly ILogger _logger;
        private readonly IBundleFactory _bundleFactory;
        private readonly HttpClient _httpClient;

        public RestHookChannel(ILogger logger, HttpClient httpClient, IBundleFactory bundleFactory)
        {
            _logger = logger;
            #pragma warning disable CA2000 // Dispose objects before losing scope
            _httpClient = new HttpClient(new HttpClientHandler() { CheckCertificateRevocationList = true }, disposeHandler: true);
            #pragma warning restore CA2000 // Dispose objects before losing scope
            _bundleFactory = bundleFactory;
        }

        public async Task PublishAsync(IReadOnlyCollection<ResourceWrapper> resources, ChannelInfo channelInfo, DateTimeOffset transactionTime, CancellationToken cancellationToken)
        {
            List<ResourceWrapper> resourceWrappers = new List<ResourceWrapper>();
            var paramater = new Parameters();
            paramater.Add("subscription", new FhirString(channelInfo.Endpoint));
            paramater.Add("type", new Code("event-notification"));

            var notificationEvent = new Parameters.ParameterComponent
            {
                Name = "notification-event",
                Part = new List<Parameters.ParameterComponent>
                {
                    new Parameters.ParameterComponent
                    {
                        Name = "timestamp",
                        Value = new FhirDateTime(transactionTime),
                    },
                },
            };
            paramater.Parameter.Add(notificationEvent);

            if (channelInfo.ContentType.Equals(SubscriptionContentType.FullResource))
            {
                resourceWrappers.AddRange(resources);
            }

            string bundle = await _bundleFactory.CreateSubscriptionBundleAsync(resourceWrappers.ToArray());

            await SendPayload(channelInfo, bundle);

            // IRestHookChannel with additional methods for handshake, heartbeat, payload for subscription notification
        }

        public async Task SendPayload(
        ChannelInfo chanelInfo,
        string contents)
        {
            HttpRequestMessage request = null!;

            // send the request to the endpoint
            try
            {
                // build our request
                request = new HttpRequestMessage()
                {
                    Method = HttpMethod.Post,
                    RequestUri = new Uri(chanelInfo.Endpoint),
                    Content = new StringContent(contents, Encoding.UTF8, chanelInfo.ContentType.ToString()),
                };

                // send our request
                HttpResponseMessage response = await _httpClient.SendAsync(request);

                // check the status code
                if ((response.StatusCode != System.Net.HttpStatusCode.OK) &&
                    (response.StatusCode != System.Net.HttpStatusCode.Accepted))
                {
                    // failure
                   _logger.LogError($"REST POST to {chanelInfo.Endpoint} failed: {response.StatusCode}");
                }
                else
                {
                    _logger.LogError($"REST POST to {chanelInfo.Endpoint} succeeded: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"REST POST {chanelInfo.ChannelType} to {chanelInfo.Endpoint} failed: {ex.Message}");
            }
            finally
            {
                if (request != null)
                {
                    request.Dispose();
                }
            }
        }
    }
}
