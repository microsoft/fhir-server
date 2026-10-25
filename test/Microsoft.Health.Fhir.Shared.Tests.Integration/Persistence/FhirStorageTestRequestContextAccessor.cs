// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.UnitTests.Features.Context;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    // A fixture-wide substitute shares mutable request properties across background jobs.
    // Use AsyncLocal ownership instead, with fresh contexts at each background operation boundary.
    internal sealed class FhirStorageTestRequestContextAccessor : FhirRequestContextAccessor
    {
        /// <inheritdoc/>
        public override IFhirRequestContext RequestContext
        {
            // Async fixture initialization cannot set the caller's AsyncLocal value.
            get => base.RequestContext ??= CreateRequestContext();
            set => base.RequestContext = value;
        }

        internal static IFhirRequestContext CreateRequestContext()
        {
            return new DefaultFhirRequestContext
            {
                BaseUri = new Uri("http://localhost/"),
                CorrelationId = Guid.NewGuid().ToString(),
                RouteName = "routeName",
                RequestHeaders = new Dictionary<string, StringValues>(),
                ResponseHeaders = new Dictionary<string, StringValues>(),
                Method = "POST",
                Uri = new Uri("http://localhost/"),
            };
        }
    }
}
