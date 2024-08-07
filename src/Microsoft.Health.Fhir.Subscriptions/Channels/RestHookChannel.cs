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
using Microsoft.Build.Framework;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Subscriptions.Models;
using Microsoft.Health.Fhir.Subscriptions.Persistence;

namespace Microsoft.Health.Fhir.Subscriptions.Channels
{
    public class RestHookChannel : ISubscriptionChannel
    {
        private HttpClient _httpClient;
        private ILogger _logger;

        public RestHookChannel(ILogger logger, HttpClient httpClient)
        {
            _logger = logger;
            _httpClient = httpClient;
        }

        public Task PublishAsync(IReadOnlyCollection<ResourceWrapper> resources, ChannelInfo channelInfo, DateTimeOffset transactionTime, CancellationToken cancellationToken)
        {
            // build up contents and send http request
            throw new NotImplementedException();

            // IRestHookChannel with additional methods for handshake, heartbeat, etc
            // private send http request method
        }

        private async Task<bool> TryNotifyRestHook(
        ITransactionDataStore store,
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

                // check for additional headers
                if ((chanelInfo.Properties != null) && chanelInfo.Properties.Any())
                {
                    // add headers
                    foreach ((string param, List<string> values) in chanelInfo.Properties)
                    {
                        if (string.IsNullOrEmpty(param) ||
                            (!values.Any()))
                        {
                            continue;
                        }

                        request.Headers.Add(param, values);
                    }
                }

                // send our request
                HttpResponseMessage response = await _httpClient.SendAsync(request);

                // check the status code
                if ((response.StatusCode != System.Net.HttpStatusCode.OK) &&
                    (response.StatusCode != System.Net.HttpStatusCode.Accepted) &&
                    (response.StatusCode != System.Net.HttpStatusCode.NoContent))
                {
                    // failure
                    e.Subscription.RegisterError($"REST POST to {chanelInfo.Endpoint} failed: {response.StatusCode}");
                    return false;
                }

                e.Subscription.LastCommunicationTicks = DateTime.Now.Ticks;

                if (e.NotificationEvents.Any())
                {
                    _logger.LogInformation(
                        $" <<< Subscription/{chanelInfo.Id}" +
                        $" POST: {chanelInfo.Endpoint}" +
                        $" Events: {string.Join(',', e.NotificationEvents.Select(ne => ne.EventNumber))}");
                }
                else
                {
                    _logger.LogInformation(
                        $" <<< Subscription/{e.Subscription.Id}" +
                        $" POST {e.NotificationType}: {e.Subscription.Endpoint}");
                }
            }
            catch (Exception ex)
            {
                e.Subscription.RegisterError($"REST POST {e.NotificationType} to {e.Subscription.Endpoint} failed: {ex.Message}");
                return false;
            }
            finally
            {
                if (request != null)
                {
                    request.Dispose();
                }
            }

            return true;
        }
    }
}
