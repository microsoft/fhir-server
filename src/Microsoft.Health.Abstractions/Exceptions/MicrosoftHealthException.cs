// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Diagnostics;

namespace Microsoft.Health.Abstractions.Exceptions
{
    public class MicrosoftHealthException : Exception
    {
        public MicrosoftHealthException(string message)
            : base(message)
        {
            Debug.Assert(!string.IsNullOrEmpty(message), "Message should not be empty");
        }
    }
}
