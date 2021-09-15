// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Core.Features.Subscriptions
{
    public interface INotificationChannel
    {
        public void NotifiyEmpty(Subscription subscription);

        public void NotifiyIdOnly(Subscription subscription, Resource[] resources);

        public void NotifiyFullResource(Subscription subscription, Resource[] resources);
    }
}
