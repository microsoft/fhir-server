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
using Microsoft.Health.Fhir.Subscriptions.Models;
using Microsoft.Health.JobManagement;

namespace Microsoft.Health.Fhir.Subscriptions.Operations
{
    [JobTypeId((int)JobType.SubscriptionsProcessing)]
    public class SubscriptionProcessingJob : IJob
    {
        private readonly IResourceToByteArraySerializer _resourceToByteArraySerializer;
        private readonly IExportDestinationClient _exportDestinationClient;
        private readonly IResourceDeserializer _resourceDeserializer;
        private readonly IFhirDataStore _dataStore;
        private readonly ILogger<SubscriptionProcessingJob> _logger;

        public SubscriptionProcessingJob(
            IResourceToByteArraySerializer resourceToByteArraySerializer,
            IExportDestinationClient exportDestinationClient,
            IResourceDeserializer resourceDeserializer,
            IFhirDataStore dataStore,
            ILogger<SubscriptionProcessingJob> logger)
        {
            _resourceToByteArraySerializer = resourceToByteArraySerializer;
            _exportDestinationClient = exportDestinationClient;
            _resourceDeserializer = resourceDeserializer;
            _dataStore = dataStore;
            _logger = logger;
        }

        public async Task<string> ExecuteAsync(JobInfo jobInfo, CancellationToken cancellationToken)
        {
            SubscriptionJobDefinition definition = jobInfo.DeserializeDefinition<SubscriptionJobDefinition>();

            if (definition.Channel == null)
            {
                return HttpStatusCode.BadRequest.ToString();
            }

            if (definition.Channel.ChannelType == SubscriptionChannelType.Storage)
            {
                try
                {
                    await _exportDestinationClient.ConnectAsync(cancellationToken, definition.Channel.Properties["container"]);

                    foreach (var resourceKey in definition.ResourceReferences)
                    {
                        var resource = await _dataStore.GetAsync(resourceKey, cancellationToken);

                        string fileName = $"{resourceKey}.json";

                        _exportDestinationClient.WriteFilePart(
                            fileName,
                            resource.RawResource.Data);

                        _exportDestinationClient.CommitFile(fileName);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogJobError(jobInfo, ex.ToString());
                    return HttpStatusCode.InternalServerError.ToString();
                }
            }
            else
            {
                return HttpStatusCode.BadRequest.ToString();
            }

            return HttpStatusCode.OK.ToString();
        }
    }
}
