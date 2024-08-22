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
using Hl7.FhirPath;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Subscriptions.Persistence;
using Microsoft.Health.Fhir.Tests.Common;
using Xunit;

namespace Microsoft.Health.Fhir.Subscriptions.Tests.Peristence
{
    public class SubscriptionUpdatorTest
    {
        [Fact]
        public void GivenAnR4BackportSubscription_WhenUpdatingStatusToActive_ThenTheInformationIsCorrect()
        {
            var subscription = CommonSamples.GetJsonSample("Subscription-Backport", FhirSpecification.R4, s => s.ToTypedElement(ModelInfo.ModelInspector));
            var updator = new SubscriptionUpdator();
            ModelInfoProvider.SetProvider(MockModelInfoProviderBuilder.Create(FhirSpecification.R4).Build());
            var updatedSubscription = updator.UpdateStatus(subscription, "active");

            Assert.Equal("active", updatedSubscription.Scalar<string>("Subscription.status"));
        }
    }
}
