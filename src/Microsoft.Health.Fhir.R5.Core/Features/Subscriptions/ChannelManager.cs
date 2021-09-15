// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Net.Http;
using Microsoft.Health.Fhir.Core.Features.Routing;

namespace Microsoft.Health.Fhir.Core.Features.Subscriptions
{
    public class ChannelManager : IChannelManager
    {
        private RestHookNotificationChannel _restHookNotificationChannel;

        public ChannelManager(IHttpClientFactory httpClientFactory, IUrlResolver urlResolver)
        {
            _restHookNotificationChannel = new RestHookNotificationChannel(httpClientFactory, urlResolver);
        }

        public RestHookNotificationChannel GetRestHookNotificationChannel()
        {
            return _restHookNotificationChannel;
        }
    }
}
