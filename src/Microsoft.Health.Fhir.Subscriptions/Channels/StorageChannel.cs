// -------------------------------------------------------------------------------------------------
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
using Microsoft.Health.Fhir.Subscriptions.Validation;

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

        public async Task PublishHandShakeAsync(SubscriptionInfo subscriptionInfo, CancellationToken cancellationToken)
        {
            try
            {
                await _exportDestinationClient.ConnectAsync(cancellationToken, subscriptionInfo.Channel.Endpoint);
            }
            catch (DestinationConnectionException ex)
            {
                throw new SubscriptionException(Resources.SubscriptionEndpointNotValid, ex);
            }
        }

        public Task PublishHeartBeatAsync(SubscriptionInfo subscriptionInfo, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
