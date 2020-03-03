// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search
{
    public class LightweightReferenceToElementResolverTests
    {
        private readonly LightweightReferenceToElementResolver _resolver;

        public LightweightReferenceToElementResolverTests()
        {
            ReferenceSearchValueParser referenceSearchValueParser = Mock.TypeWithArguments<ReferenceSearchValueParser>();

            _resolver = new LightweightReferenceToElementResolver(referenceSearchValueParser, ModelInfoProvider.Instance);
        }

        [Fact]
        public void GivenAValidReference_WhenConvertingAReferenceToTypedElement_ThenTheResultIsValid()
        {
            var patentRef = "Patient/1234";

            var result = _resolver.Resolve(patentRef);

            Assert.Equal("Patient", result.InstanceType);
            Assert.Equal("1234", result.Children("id").Single().Value);
        }

        [Fact]
        public void GivenAValidReferenceWithHistory_WhenConvertingAReferenceToTypedElement_ThenTheResultIsValid()
        {
            var patentRef = "Patient/1234/_history/56789";

            var result = _resolver.Resolve(patentRef);

            Assert.Equal("Patient", result.InstanceType);
            Assert.Equal("1234", result.Children("id").Single().Value);
        }

        [InlineData("Test/1234")]
        [InlineData("Patient")]
        [InlineData("")]
        [InlineData(null)]
        [Theory]
        public void GivenAnInvalidReference_WhenConvertingAReferenceToTypedElement_ThenTheResultIsNull(string patentRef)
        {
            var result = _resolver.Resolve(patentRef);

            Assert.Null(result);
        }
    }
}
