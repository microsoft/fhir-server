// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Health.Fhir.Api.Features.Operations.Import;
using Microsoft.Health.Fhir.Core.Features.Operations.Import.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Shared.Api.UnitTests.Features.Operations.Import
{
    [Trait(Traits.OwningTeam, OwningTeam.FhirImport)]
    [Trait(Traits.Category, Categories.Import)]
    public class ImportRequestExtensionsTests
    {
        [Fact]
        public void GivenImportRequestInParamtersFormat_WhenConvert_ThenImportRequestShouldBeReturned()
        {
            ImportRequest input = new ImportRequest();
            input.InputFormat = "test";
            input.Force = true;
            input.Mode = "test";
            input.InputSource = new Uri("http://dummy");
            input.Input = new List<InputResource>() { new InputResource() { Etag = "etag", Type = "type", Url = new Uri("http://dummy/resource") } };
            input.StorageDetail = new ImportRequestStorageDetail() { Type = "blob" };

            ImportRequest output = input.ToParameters().ExtractImportRequest();
            Assert.Equal(input.InputFormat, output.InputFormat);
            Assert.Equal(input.InputSource, output.InputSource);
            Assert.Equal(input.Force, output.Force);
            Assert.Equal(input.Mode, output.Mode);
            Assert.Equal(input.StorageDetail.Type, output.StorageDetail.Type);
            Assert.Equal(input.Input[0].Type, output.Input[0].Type);
            Assert.Equal(input.Input[0].Url, output.Input[0].Url);
            Assert.Equal(input.Input[0].Etag, output.Input[0].Etag);
        }

        [Fact]
        public void GivenEmptyImportRequestInParamtersFormat_WhenConvert_ThenDefaultValueShouldBeFilled()
        {
            ImportRequest input = new ImportRequest();

            ImportRequest output = input.ToParameters().ExtractImportRequest();
            Assert.Equal(ImportRequestExtensions.DefaultInputFormat, output.InputFormat);
            Assert.Equal(ImportRequestExtensions.DefaultStorageDetailType, output.StorageDetail.Type);
        }
    }
}
