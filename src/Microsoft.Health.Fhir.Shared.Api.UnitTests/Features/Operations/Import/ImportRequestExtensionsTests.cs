// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Api.Features.Operations.Import;
using Microsoft.Health.Fhir.Core.Features.Operations.Import.Models;
using Xunit;

namespace Microsoft.Health.Fhir.Shared.Api.UnitTests.Features.Operations.Import
{
    public class ImportRequestExtensionsTests
    {
        [Fact]
        public void GivenImportReuqestInParamtersFormat_WhenConvert_ImportRequestShouldBeReturned()
        {
            Parameters paramters = new Parameters();
            var formatParam = new Parameters.ParameterComponent();
            formatParam.Name = ImportRequestExtensions.InputFormatParamterName;
            formatParam.Value = new FhirString(ImportRequestExtensions.DefaultInputFormat);

            paramters.Parameter.Add(formatParam);

            ImportRequest output = paramters.ExtractImportRequest();
            Assert.Equal(ImportRequestExtensions.DefaultInputFormat, output.InputFormat);
        }
    }
}
