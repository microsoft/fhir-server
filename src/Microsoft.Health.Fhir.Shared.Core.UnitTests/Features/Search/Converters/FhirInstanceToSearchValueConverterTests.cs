// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Search.Converters;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.Converters
{
    public abstract class FhirInstanceToSearchValueConverterTests<TElement>
        where TElement : Element, new()
    {
        protected TElement Element { get; } = new TElement();

        protected virtual ITypedElement TypedElement => Element.ToTypedElement();

        protected abstract Task<ITypedElementToSearchValueConverter> GetTypeConverterAsync();

        [Fact]
        public async Task GivenANullValue_WhenConverted_ThenNoSearchValueShouldBeCreated()
        {
            IEnumerable<ISearchValue> values = (await GetTypeConverterAsync()).ConvertTo(null);

            Assert.NotNull(values);
            Assert.Empty(values);
        }

        protected async Task Test<TValue>(Action<TElement> setup, Action<TValue, ISearchValue> validator, params TValue[] expected)
        {
            setup(Element);

            IEnumerable<ISearchValue> values = (await GetTypeConverterAsync()).ConvertTo(TypedElement);

            Assert.NotNull(values);
            Assert.Collection(
                values,
                expected.Select(e => new Action<ISearchValue>(sv => validator(e, sv))).ToArray());
        }

        protected async Task Test(Action<TElement> setup)
        {
            setup(Element);

            IEnumerable<ISearchValue> values = (await GetTypeConverterAsync()).ConvertTo(TypedElement);

            Assert.NotNull(values);
            Assert.Empty(values);
        }
    }
}
