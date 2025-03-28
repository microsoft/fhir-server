// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Core.Exceptions
{
    public class InitializationException : Exception
    {
        public InitializationException(string message)
            : base(message)
        {
        }
    }
}
