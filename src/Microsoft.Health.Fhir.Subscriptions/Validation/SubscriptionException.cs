// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Health.Abstractions.Exceptions;

namespace Microsoft.Health.Fhir.Subscriptions.Validation
{
    public class SubscriptionException : MicrosoftHealthException
    {
        public SubscriptionException(string message)
            : base(message)
        {
        }

        public SubscriptionException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
