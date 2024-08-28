// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Subscriptions.Models
{
    public class SubscriptionModelConverterR4 : ISubscriptionModelConverter
    {
        private const string MetaString = "http://hl7.org/fhir/uv/subscriptions-backport/StructureDefinition/backport-subscription";
        private const string CriteriaString = "http://azurehealthcareapis.com/data-extentions/SubscriptionTopics/transactions";
        private const string CriteriaExtensionString = "http://hl7.org/fhir/uv/subscriptions-backport/StructureDefinition/backport-filter-criteria";
        private const string ChannelTypeString = "http://hl7.org/fhir/uv/subscriptions-backport/StructureDefinition/backport-channel-type";

        // private const string AzureChannelTypeString = "http://azurehealthcareapis.com/data-extentions/subscription-channel-type";
        private const string PayloadTypeString = "http://hl7.org/fhir/uv/subscriptions-backport/StructureDefinition/backport-payload-content";
        private const string MaxCountString = "http://hl7.org/fhir/uv/subscriptions-backport/StructureDefinition/backport-max-count";
        private const string HeartBeatString = "http://hl7.org/fhir/uv/subscriptions-backport/StructureDefinition/backport-heartbeat-period";

        public SubscriptionInfo Convert(ResourceElement resource)
        {
            var profile = resource.Scalar<string>("Subscription.meta.profile");

            if (profile != MetaString)
            {
                return null;
            }

            var criteria = resource.Scalar<string>($"Subscription.criteria");

            if (criteria != CriteriaString)
            {
                return null;
            }

            var criteriaExt = resource.Scalar<string>($"Subscription.criteria.extension.where(url = '{CriteriaExtensionString}').value");
            var channelTypeExt = resource.Scalar<string>($"Subscription.channel.type.extension.where(url = '{ChannelTypeString}').value.code");
            var payloadType = resource.Scalar<string>($"Subscription.channel.payload.extension.where(url = '{PayloadTypeString}').value");
            var maxCount = resource.Scalar<int?>($"Subscription.channel.extension.where(url = '{MaxCountString}').value");
            var heartBeatSpan = resource.Scalar<int?>($"Subscription.channel.extension.where(url = '{HeartBeatString}').value");
            var resourceId = resource.Scalar<string>("Subscription.id");
            var status = resource.Scalar<string>("Subscription.status") switch
            {
                "active" => SubscriptionStatus.Active,
                "requested" => SubscriptionStatus.Requested,
                "error" => SubscriptionStatus.Error,
                "off" => SubscriptionStatus.Off,
                _ => SubscriptionStatus.None,
            };
            var topic = new Uri(resource.Scalar<string>("Subscription.criteria"));

            var channelInfo = new ChannelInfo
            {
                Endpoint = resource.Scalar<string>($"Subscription.channel.endpoint"),
                ChannelType = channelTypeExt switch
                {
                    "azure-storage" => SubscriptionChannelType.Storage,
                    "azure-lake-storage" => SubscriptionChannelType.DatalakeContract,
                    "rest-hook" => SubscriptionChannelType.RestHook,
                    _ => SubscriptionChannelType.None,
                },
                ContentType = payloadType switch
                {
                    "full-resource" => SubscriptionContentType.FullResource,
                    "id-only" => SubscriptionContentType.IdOnly,
                    _ => SubscriptionContentType.Empty,
                },
                MaxCount = maxCount ?? 100,
            };

            if (heartBeatSpan.HasValue)
            {
                channelInfo.HeartBeatPeriod = TimeSpan.FromSeconds(heartBeatSpan.Value);
            }

            var info = new SubscriptionInfo(criteriaExt, channelInfo, topic, resourceId, status);

            return info;
        }
    }
}
