// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Search.Converters;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.UnitTests.Features.Search.Converters;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;
using static Microsoft.Health.Fhir.Tests.Common.Search.SearchValueValidationHelper;

using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search
{
    [Trait("Traits.OwningTeam", OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public sealed class ExtensionToQuantitySearchValueConverterTests : FhirInstanceToSearchValueConverterTests<Extension>
    {
        protected override async Task<ITypedElementToSearchValueConverter> GetTypeConverterAsync()
        {
            FhirTypedElementToSearchValueConverterManager fhirTypedElementToSearchValueConverterManager = await SearchParameterFixtureData.GetFhirTypedElementToSearchValueConverterManagerAsync();
            fhirTypedElementToSearchValueConverterManager.TryGetConverter("Extension", typeof(QuantitySearchValue), out ITypedElementToSearchValueConverter extensionConverter);

            return extensionConverter;
        }

        public static IEnumerable<object[]> GetQuantityExtensionDataSource()
        {
            yield return new object[] { new Extension("test", new Quantity(100.123456m, "unit", "system")), new Quantity(100.123456m, "unit", "system") };
        }

        [Theory]
        [MemberData(nameof(GetQuantityExtensionDataSource))]
        public async Task GivenAQuantityExtension_WhenConverted_ThenAQuantitySearchValueShouldBeCreated(Extension extension, Quantity expected)
        {
            await TestExtensionAsync(
                ext =>
                {
                    ext.Url = extension.Url;
                    ext.Value = extension.Value;
                },
                ValidateQuantity,
                expected);
        }
    }
}
