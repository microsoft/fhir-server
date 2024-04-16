// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Subscriptions.Models
{
    public enum SubscriptionChannelType
    {
        None = 0,
        RestHook = 1,
        WebSocket = 2,
        Email = 3,
        FhirMessaging = 4,

        // Custom Channels
        EventGrid = 5,
        Storage = 6,
        DatalakeContract = 7,
    }
}
