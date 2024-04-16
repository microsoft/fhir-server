// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EnsureThat;

namespace Microsoft.Health.Fhir.Subscriptions.Models
{
    public class SubscriptionInfo
    {
        public SubscriptionInfo(string filterCriteria)
        {
            FilterCriteria = EnsureArg.IsNotNullOrEmpty(filterCriteria, nameof(filterCriteria));
        }

        public string FilterCriteria { get; set; }
    }
}
