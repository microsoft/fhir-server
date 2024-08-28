// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hl7.Fhir.Utility;

namespace Microsoft.Health.Fhir.Subscriptions.Models
{
    public enum SubscriptionStatus
    {
        [EnumLiteral("requested")]
        Requested,
        [EnumLiteral("active")]
        Active,
        [EnumLiteral("error")]
        Error,
        [EnumLiteral("off")]
        Off,
        None,
    }
}
