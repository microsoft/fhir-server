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
    [Trait(Traits.Category, Categories.Search)]
    public sealed class ExtensionToNumberSearchValueConverterTests : FhirInstanceToSearchValueConverterTests<Extension>
    {
        protected override async Task<ITypedElementToSearchValueConverter> GetTypeConverterAsync()
        {
            FhirTypedElementToSearchValueConverterManager fhirTypedElementToSearchValueConverterManager = await SearchParameterFixtureData.GetFhirTypedElementToSearchValueConverterManagerAsync();
            fhirTypedElementToSearchValueConverterManager.TryGetConverter("Extension", typeof(NumberSearchValue), out ITypedElementToSearchValueConverter extensionConverter);

            return extensionConverter;
        }

        public static IEnumerable<object[]> GetNumberExtensionDataSource()
        {
            yield return new object[] { new Extension("test", new UnsignedInt(1)), 1M };
            yield return new object[] { new Extension("test", new PositiveInt(1)), 1M };
            yield return new object[] { new Extension("test", new Integer(500)), 500 };
        }

        [Theory]
        [MemberData(nameof(GetNumberExtensionDataSource))]
        public async Task GivenANumberExtension_WhenConverted_ThenANumberSearchValueShouldBeCreated(Extension extension, decimal expected)
        {
            await TestExtensionAsync(
                ext =>
                {
                    ext.Url = extension.Url;
                    ext.Value = extension.Value;
                },
                ValidateNumber,
                expected);
        }
    }
}
