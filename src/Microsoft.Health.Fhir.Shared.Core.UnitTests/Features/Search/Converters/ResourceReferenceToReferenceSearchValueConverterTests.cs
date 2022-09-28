// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Search.Converters;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.Converters
{
    [Trait("Traits.OwningTeam", OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class ResourceReferenceToReferenceSearchValueConverterTests
    {
        private readonly IReferenceSearchValueParser _referenceSearchValueParser = Substitute.For<IReferenceSearchValueParser>();
        private readonly ResourceReferenceToReferenceSearchValueConverter _converter;
        private readonly ResourceReference _reference = new ResourceReference();

        public ResourceReferenceToReferenceSearchValueConverterTests()
        {
            _converter = new ResourceReferenceToReferenceSearchValueConverter(_referenceSearchValueParser);
        }

        [Fact]
        public void GivenANullValue_WhenConverted_ThenNoSearchValueShouldBeCreated()
        {
            IEnumerable<ISearchValue> values = _converter.ConvertTo(null);

            Assert.NotNull(values);
            Assert.Empty(values);
        }

        [Fact]
        public void GivenAResourceReferenceWithNoValue_WhenConverted_ThenNoSearchValueShouldBeCreated()
        {
            Test(r => r.Reference = null);
        }

        [Fact]
        public void GivenAResourceReferenceWithContainedReference_WhenConverted_ThenNoSearchValueShouldBeCreated()
        {
            Test(r => r.Reference = "#patient");
        }

        [Fact]
        public void GivenAResourceReferenceWithReference_WhenConverted_ThenAReferenceSearchValueShouldBeCreated()
        {
            const string reference = "Patient/123";

            var expectedSearchValue = new ReferenceSearchValue(ReferenceKind.InternalOrExternal, null, null, "123");

            _referenceSearchValueParser.Parse(reference).Returns(expectedSearchValue);

            _reference.Reference = reference;

            IEnumerable<ISearchValue> results = _converter.ConvertTo(_reference.ToTypedElement());

            Assert.NotNull(results);
            Assert.Collection(
                results,
                e => Assert.Same(expectedSearchValue, e));
        }

        private void Test(Action<ResourceReference> setup)
        {
            setup(_reference);

            IEnumerable<ISearchValue> values = _converter.ConvertTo(_reference.ToTypedElement());

            Assert.NotNull(values);
            Assert.Empty(values);
        }
    }
}
