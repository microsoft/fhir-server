// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.ExportDestinationClient;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Subscriptions.Models;

namespace Microsoft.Health.Fhir.Subscriptions.Channels
{
    [ChannelType(SubscriptionChannelType.DatalakeContract)]
    public class DataLakeChannel : ISubscriptionChannel
    {
        private readonly IExportDestinationClient _exportDestinationClient;
        private readonly IResourceDeserializer _resourceDeserializer;

        public DataLakeChannel(IExportDestinationClient exportDestinationClient, IResourceDeserializer resourceDeserializer)
        {
            _exportDestinationClient = exportDestinationClient;
            _resourceDeserializer = resourceDeserializer;
        }

        public async Task PublishAsync(IReadOnlyCollection<ResourceWrapper> resources, ChannelInfo channelInfo, DateTimeOffset transactionTime, CancellationToken cancellationToken)
        {
            try
            {
                await _exportDestinationClient.ConnectAsync(cancellationToken, channelInfo.Properties["container"]);

                IReadOnlyList<IGrouping<string, ResourceWrapper>> resourceGroupedByResourceType = resources.GroupBy(x => x.ResourceTypeName.ToLower(CultureInfo.InvariantCulture)).ToList();

                DateTimeOffset transactionTimeInUtc = transactionTime.ToUniversalTime();

                foreach (IGrouping<string, ResourceWrapper> groupOfResources in resourceGroupedByResourceType)
                {
                    string blobName = $"{groupOfResources.Key}/{transactionTimeInUtc.Year:D4}/{transactionTimeInUtc.Month:D2}/{transactionTimeInUtc.Day:D2}/{transactionTimeInUtc.ToString("yyyy-MM-ddTHH.mm.ss.fffZ")}.ndjson";

                    foreach (ResourceWrapper item in groupOfResources)
                    {
                        string json = item.RawResource.Data;

                        /*
                        // TODO: Add logic to handle soft-deleted resources.
                        if (item.IsDeleted)
                        {
                            ResourceElement element = _resourceDeserializer.Deserialize(item);
                        }
                        */

                        _exportDestinationClient.WriteFilePart(blobName, json);
                    }

                    _exportDestinationClient.Commit();
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failure in DatalakeChannel", ex);
            }
        }
    }
}
