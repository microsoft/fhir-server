// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Converters;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.UnitTests.Features.Search.Converters.NodeConverterTests;
using Xunit;
using static Microsoft.Health.Fhir.Tests.Common.Search.SearchValueValidationHelper;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.Converters
{
    public class CodeOfTNodeToTokenSearchValueTypeConverterTests : FhirNodeInstanceToSearchValueTypeConverterTests<Code<ResourceType>>
    {
        public CodeOfTNodeToTokenSearchValueTypeConverterTests()
            : base(new CodeNodeToTokenSearchValueTypeConverter(CodeSystemResolver()))
        {
        }

        private static CodeSystemResolver CodeSystemResolver()
        {
            var resolver = new CodeSystemResolver(ModelInfoProvider.Instance);
            resolver.Start();
            return resolver;
        }

        [Fact]
        public void GivenACode_WhenConverted_ThenATokenSearchValueShouldBeCreated()
        {
            Test(
                code => code.Value = ResourceType.Patient,
                ValidateToken,
                new Token(null, "Patient"));
        }

        [Fact]
        public void GivenACodeAndSystem_WhenConverted_ThenATokenSearchValueShouldBeCreated()
        {
            Test(
                code => code.Value = ResourceType.Patient,
                ValidateToken,
                new Token("http://hl7.org/fhir/resource-types", "Patient"));
        }

        [Fact]
        public void GivenANullCode_WhenConverted_ThenNullValueReturned()
        {
            Test(
                code => code.Value = null,
                ValidateNull,
                new Code<ResourceType>(null));
        }
    }
}
