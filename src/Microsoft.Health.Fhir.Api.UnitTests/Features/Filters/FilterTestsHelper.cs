// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Controllers;
using Microsoft.Health.Fhir.Api.Features.Context;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Filters
{
    public static class FilterTestsHelper
    {
        public static FhirController CreateMockFhirController()
        {
            return Mock.TypeWithArguments<FhirController>(Options.Create(new FeatureConfiguration()));
        }

        [AllowAnonymous]
        public static void MethodWithAnonymousAttribute()
        {
            return;
        }

        [AuditEventSubType(AuditEventSubType.Update)]
        public static void MethodWithAuditEventAttribute()
        {
            return;
        }

        public static void MethodWithNoAttribute()
        {
            return;
        }
    }
}
