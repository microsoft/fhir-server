// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Subscriptions.Models;
using Microsoft.Health.Fhir.Subscriptions.Persistence;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Subscriptions.Tests.Peristence
{
    public class SubscriptionManagerTests
    {
        private IModelInfoProvider _modelInfo;

        public SubscriptionManagerTests()
        {
            _modelInfo = MockModelInfoProviderBuilder
                .Create(FhirSpecification.R4)
                .AddKnownTypes(KnownResourceTypes.Subscription)
                .Build();
        }

        [Fact]
        public void GivenAnR4BackportSubscription_WhenConvertingToInfo_ThenTheInformationIsCorrect()
        {
            var subscription = CommonSamples.GetJsonSample("Subscription-Backport", FhirSpecification.R4, s => s.ToTypedElement(ModelInfo.ModelInspector));

            var info = SubscriptionManager.ConvertToInfo(subscription);

            Assert.Equal("Patient", info.FilterCriteria);
            Assert.Equal("sync-all", info.Channel.Endpoint);
            Assert.Equal(20, info.Channel.MaxCount);
            Assert.Equal(SubscriptionContentType.FullResource, info.Channel.ContentType);
            Assert.Equal(SubscriptionChannelType.Storage, info.Channel.ChannelType);
        }
    }
}
