// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Web;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    /// <summary>
    /// An attribute to be placed on a custom <see cref="Startup"/> type for in-proc E2E tests, when
    /// the in-proc server must only trust forwarded headers from a configured network that the test
    /// client is not part of. Forwarded headers sent by the test client are therefore ignored.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class RequiresTrustedForwardersAttribute : Attribute
    {
    }
}
