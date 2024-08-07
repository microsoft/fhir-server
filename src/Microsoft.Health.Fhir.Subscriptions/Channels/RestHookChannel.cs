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
    }
}
