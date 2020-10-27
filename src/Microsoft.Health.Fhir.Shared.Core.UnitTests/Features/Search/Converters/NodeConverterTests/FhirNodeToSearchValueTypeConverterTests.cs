// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Search.Converters;
using Microsoft.Health.Fhir.Core.UnitTests.Features.Search.Converters.NodeConverterTests;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.Converters
{
    public abstract class FhirNodeToSearchValueTypeConverterTests<TTypeConverter, TElement> : FhirNodeInstanceToSearchValueTypeConverterTests<TElement>
        where TTypeConverter : IFhirNodeToSearchValueTypeConverter, new()
        where TElement : Element, new()
    {
        protected override Task<IFhirNodeToSearchValueTypeConverter> GetTypeConverterAsync()
        {
            return Task.FromResult((IFhirNodeToSearchValueTypeConverter)new TTypeConverter());
        }
    }
}
