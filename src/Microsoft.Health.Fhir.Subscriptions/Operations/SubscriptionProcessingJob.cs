// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Build.Framework;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.ExportDestinationClient;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Subscriptions.Channels;
using Microsoft.Health.Fhir.Subscriptions.Models;
using Microsoft.Health.JobManagement;

namespace Microsoft.Health.Fhir.Subscriptions.Operations
{
    [JobTypeId((int)JobType.SubscriptionsProcessing)]
    public class SubscriptionProcessingJob : IJob
    {
        private readonly StorageChannelFactory _storageChannelFactory;
        private readonly IFhirDataStore _dataStore;

        public SubscriptionProcessingJob(StorageChannelFactory storageChannelFactory, IFhirDataStore dataStore)
        {
            _storageChannelFactory = storageChannelFactory;
            _dataStore = dataStore;
        }

        public async Task<string> ExecuteAsync(JobInfo jobInfo, CancellationToken cancellationToken)
        {
            SubscriptionJobDefinition definition = jobInfo.DeserializeDefinition<SubscriptionJobDefinition>();

            if (definition.Channel == null)
            {
                return HttpStatusCode.BadRequest.ToString();
            }

            var allResources = await Task.WhenAll(
                definition.ResourceReferences
                .Select(async x => await _dataStore.GetAsync(x, cancellationToken)));

            var channel = _storageChannelFactory.Create(definition.Channel.ChannelType);
            await channel.PublishAsync(allResources, definition.Channel, definition.VisibleDate, cancellationToken);

            return HttpStatusCode.OK.ToString();
        }
    }
}
