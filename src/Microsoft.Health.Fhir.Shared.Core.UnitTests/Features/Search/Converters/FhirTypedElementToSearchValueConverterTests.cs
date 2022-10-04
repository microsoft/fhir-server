// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Search.Converters;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.Converters
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public abstract class FhirTypedElementToSearchValueConverterTests<TTypeConverter, TElement> : FhirInstanceToSearchValueConverterTests<TElement>
        where TTypeConverter : ITypedElementToSearchValueConverter, new()
        where TElement : Element, new()
    {
        protected override Task<ITypedElementToSearchValueConverter> GetTypeConverterAsync()
        {
            return Task.FromResult((ITypedElementToSearchValueConverter)new TTypeConverter());
        }
    }
}
