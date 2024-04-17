// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.ExportDestinationClient;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Subscriptions.Models;

namespace Microsoft.Health.Fhir.Subscriptions.Channels
{
    [ChannelType(SubscriptionChannelType.DatalakeContract)]
    public class DataLakeChannel : ISubscriptionChannel
    {
        private readonly IExportDestinationClient _exportDestinationClient;

        public DataLakeChannel(IExportDestinationClient exportDestinationClient)
        {
            _exportDestinationClient = exportDestinationClient;
        }

        public async Task PublishAsync(IReadOnlyCollection<ResourceWrapper> resources, ChannelInfo channelInfo, DateTimeOffset transactionTime, CancellationToken cancellationToken)
        {
            try
            {
                await _exportDestinationClient.ConnectAsync(cancellationToken, channelInfo.Properties["container"]);

                IReadOnlyList<IGrouping<string, ResourceWrapper>> resourceGroupedByResourceType = resources.GroupBy(x => x.ResourceTypeName.ToLower(CultureInfo.InvariantCulture)).ToList();

                foreach (IGrouping<string, ResourceWrapper> groupOfResources in resourceGroupedByResourceType)
                {
                    string blobName = $"{groupOfResources.Key}/{transactionTime.Year:D4}/{transactionTime.Month:D2}/{transactionTime.Day:D2}/{transactionTime.ToUniversalTime().ToString("yyyy-MM-ddTHH.mm.ss.fffZ")}.ndjson";

                    foreach (ResourceWrapper item in groupOfResources)
                    {
                        // TODO: implement the soft-delete property addition.
                        string json = item.RawResource.Data;

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
