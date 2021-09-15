// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Core.Features.Subscriptions
{
    public interface ISubscriptionListener
    {
        public bool TryAddListener(Resource resource);

        public void AddListener(Subscription subscription);

        public System.Threading.Tasks.Task Evaluate(Resource resource, SubscriptionTopic.InteractionTrigger interaction);
    }
}
