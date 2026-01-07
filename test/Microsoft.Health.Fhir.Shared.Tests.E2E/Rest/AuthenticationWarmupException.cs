// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    /// <summary>
    /// Exception thrown when authentication warmup fails during E2E test initialization.
    /// This exception is designed to fail the entire test assembly fast when authentication
    /// is not working, rather than running hundreds of tests that will all fail with 401 errors.
    /// </summary>
    public class AuthenticationWarmupException : Exception
    {
        public AuthenticationWarmupException(string message)
            : base(message)
        {
        }

        public AuthenticationWarmupException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
