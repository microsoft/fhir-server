// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using Microsoft.Health.Fhir.Tests.Common;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Audit
{
    /// <summary>
    /// Provides R4 specific tests.
    /// </summary>
    [Trait(Traits.Category, Categories.Batch)]
    public partial class AuditTests
    {
        private const HttpStatusCode IfMatchFailureStatus = HttpStatusCode.PreconditionFailed;
    }
}
