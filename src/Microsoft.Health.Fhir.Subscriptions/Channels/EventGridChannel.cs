// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure.Messaging.EventGrid;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Subscriptions.Models;

namespace Microsoft.Health.Fhir.Subscriptions.Channels
{
    [ChannelType(SubscriptionChannelType.EventGrid)]
    public class EventGridChannel : ISubscriptionChannel
    {
        public EventGridChannel()
        {
        }

        public Task PublishAsync(IReadOnlyCollection<ResourceWrapper> resources, SubscriptionInfo subscriptionInfo, DateTimeOffset transactionTime, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task PublishHandShakeAsync(SubscriptionInfo subscriptionInfo)
        {
            return Task.CompletedTask;
        }

        public Task PublishHeartBeatAsync(SubscriptionInfo subscriptionInfo)
        {
            return Task.CompletedTask;
        }

        /*
        public EventGridEvent CreateEventGridEvent(ResourceWrapper rcd)
        {
            EnsureArg.IsNotNull(rcd);

            string resourceId = rcd.ResourceId;
            string resourceTypeName = rcd.ResourceTypeName;
            string resourceVersion = rcd.Version;
            string dataVersion = resourceVersion.ToString(CultureInfo.InvariantCulture);
            string fhirAccountDomainName = _workerConfiguration.FhirAccount;

            string eventSubject = GetEventSubject(rcd);
            string eventType = _workerConfiguration.ResourceChangeTypeMap[rcd.ResourceChangeTypeId];
            string eventGuid = rcd.GetSha256BasedGuid();

            // The swagger specification requires the response JSON to have all properties use camelcasing
            // and hence the dataPayload properties below have to use camelcase.
            var dataPayload = new BinaryData(new
            {
                resourceType = resourceTypeName,
                resourceFhirAccount = fhirAccountDomainName,
                resourceFhirId = resourceId,
                resourceVersionId = resourceVersion,
            });

            return new EventGridEvent(
                subject: eventSubject,
                eventType: eventType,
                dataVersion: dataVersion,
                data: dataPayload)
            {
                Topic = _workerConfiguration.EventGridTopic,
                Id = eventGuid,
                EventTime = rcd.Timestamp,
            };
        }

        public string GetEventSubject(ResourceChangeData rcd)
        {
            EnsureArg.IsNotNull(rcd);

            // Example: "myfhirserver.contoso.com/Observation/cb875194-1195-4617-b2e9-0966bd6b8a10"
            var fhirAccountDomainName = "fhirevents";
            var subjectSegements = new string[] { fhirAccountDomainName, rcd.ResourceTypeName, rcd.ResourceId };
            var subject = string.Join("/", subjectSegements);
            return subject;
        }
        */
    }
}
