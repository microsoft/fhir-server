﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Controllers;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Test.Utilities;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Filters
{
    public static class FilterTestsHelper
    {
        public static FhirController CreateMockFhirController()
        {
            return Mock.TypeWithArguments<FhirController>(
                new FhirRequestContextAccessor(),
                Options.Create(new FeatureConfiguration()));
        }

        public static ExportController CreateMockExportController()
        {
            return Mock.TypeWithArguments<ExportController>(
                new FhirRequestContextAccessor(),
                Options.Create(new OperationsConfiguration()),
                Options.Create(new FeatureConfiguration()));
        }

        public static ImportController CreateMockBulkImportController()
        {
            return Mock.TypeWithArguments<ImportController>(
                new FhirRequestContextAccessor(),
                Options.Create(new OperationsConfiguration()),
                Options.Create(new FeatureConfiguration()));
        }
    }
}
