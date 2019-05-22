// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Search.Converters;
using Xunit;
using static Microsoft.Health.Fhir.Tests.Common.Search.SearchValueValidationHelper;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.Converters
{
    public class CodeOfTToTokenSearchValueTypeConverterTests : FhirElementToSearchValueTypeConverterTests<CodeOfTToTokenSearchValueTypeConverter, Code<ResourceType>>
    {
        [Fact]
        public void GivenACode_WhenConverted_ThenATokenSearchValueShouldBeCreated()
        {
            Test(
                code => code.Value = ResourceType.Patient,
                ValidateToken,
                new Token("http://hl7.org/fhir/resource-types", "Patient"));
        }
    }
}
