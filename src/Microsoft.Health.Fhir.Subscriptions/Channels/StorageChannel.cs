﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.ExportDestinationClient;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Subscriptions.Models;

namespace Microsoft.Health.Fhir.Subscriptions.Channels
{
    [ChannelType(SubscriptionChannelType.Storage)]
    public class StorageChannel : ISubscriptionChannel
    {
        private readonly IExportDestinationClient _exportDestinationClient;

        public StorageChannel(
            IExportDestinationClient exportDestinationClient)
        {
            _exportDestinationClient = exportDestinationClient;
        }

        public async Task PublishAsync(IReadOnlyCollection<ResourceWrapper> resources, SubscriptionInfo subscriptionInfo, DateTimeOffset transactionTime, CancellationToken cancellationToken)
        {
            await _exportDestinationClient.ConnectAsync(cancellationToken, subscriptionInfo.Channel.Endpoint);

            foreach (var resource in resources)
            {
                string fileName = $"{resource.ToResourceKey()}.json";

                _exportDestinationClient.WriteFilePart(
                    fileName,
                    resource.RawResource.Data);

                _exportDestinationClient.CommitFile(fileName);
            }
        }

        public Task PublishHandShakeAsync(SubscriptionInfo subscriptionInfo)
        {
            throw new NotImplementedException();
        }

        public Task PublishHeartBeatAsync(SubscriptionInfo subscriptionInfo)
        {
            throw new NotImplementedException();
        }
    }
}
