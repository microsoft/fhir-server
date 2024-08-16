﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Liquid.Converter.DotLiquids;
using Microsoft.Health.Fhir.Subscriptions.Models;

namespace Microsoft.Health.Fhir.Subscriptions.Validation
{
    public interface ISubscriptionChannelValidator
    {
        Task Validate(SubscriptionInfo subscriptionInfo);
    }
}
