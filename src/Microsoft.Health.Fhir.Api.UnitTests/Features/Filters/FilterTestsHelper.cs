// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Controllers;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Tests.Common;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Filters
{
    public static class FilterTestsHelper
    {
        public static FhirController CreateMockFhirController()
        {
            return Mock.TypeWithArguments<FhirController>(Options.Create(new FeatureConfiguration()));
        }

        public static ExportController CreateMockExportController()
        {
            return Mock.TypeWithArguments<ExportController>(Options.Create(new OperationsConfiguration()));
        }
    }
}
