// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Specification;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Subscriptions;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Subscriptions.Persistence
{
    public class SubscriptionUpdator : ISubscriptionUpdator
    {
        public ResourceElement UpdateStatus(ResourceElement subscription, string status)
        {
            var subscriptionElementNode = ElementNode.FromElement(subscription.Instance);
            var oldStatusNode = (ElementNode)subscriptionElementNode.Children("status").FirstOrDefault();
            var newStatus = ElementNode.FromElement(oldStatusNode);
            newStatus.Value = status;
            subscriptionElementNode.Replace(ModelInfoProvider.Instance.StructureDefinitionSummaryProvider, oldStatusNode, newStatus);

            return subscriptionElementNode.ToResourceElement();
        }
    }
}
