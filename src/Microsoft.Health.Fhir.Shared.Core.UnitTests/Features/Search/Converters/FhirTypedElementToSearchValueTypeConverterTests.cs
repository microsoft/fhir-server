// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Search.Converters;

using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.Converters
{
    public abstract class FhirTypedElementToSearchValueTypeConverterTests<TTypeConverter, TElement> : FhirInstanceToSearchValueTypeConverterTests<TElement>
        where TTypeConverter : ITypedElementToSearchValueTypeConverter, new()
        where TElement : Element, new()
    {
        protected override Task<ITypedElementToSearchValueTypeConverter> GetTypeConverterAsync()
        {
            return Task.FromResult((ITypedElementToSearchValueTypeConverter)new TTypeConverter());
        }
    }
}
