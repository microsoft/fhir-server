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
    /// the tests require that the in-proc server operates on an isolated database.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class RequiresIsolatedDatabaseAttribute : Attribute
    {
    }
}
