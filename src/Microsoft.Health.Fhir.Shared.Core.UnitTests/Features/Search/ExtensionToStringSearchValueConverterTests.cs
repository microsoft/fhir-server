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
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public sealed class ExtensionToStringSearchValueConverterTests : FhirInstanceToSearchValueConverterTests<Extension>
    {
        [Trait(Traits.Category, Categories.Search)]
        protected override async Task<ITypedElementToSearchValueConverter> GetTypeConverterAsync()
        {
            FhirTypedElementToSearchValueConverterManager fhirTypedElementToSearchValueConverterManager = await SearchParameterFixtureData.GetFhirTypedElementToSearchValueConverterManagerAsync();
            fhirTypedElementToSearchValueConverterManager.TryGetConverter("Extension", typeof(StringSearchValue), out ITypedElementToSearchValueConverter extensionConverter);

            return extensionConverter;
        }

        public static IEnumerable<object[]> GetStringExtensionDataSource()
        {
            yield return new object[] { new Extension("test", new Address { City = "Seattle" }), "Seattle" };
            yield return new object[] { new Extension("test", new HumanName { Given = new[] { "given" } }), "given" };
            yield return new object[] { new Extension("test", new Markdown("value")), "value" };
            yield return new object[] { new Extension("test", new FhirString("value")), "value" };
        }

        [Theory]
        [MemberData(nameof(GetStringExtensionDataSource))]
        public async Task GivenAStringExtension_WhenConverted_ThenAStringSearchValueShouldBeCreated(Extension extension, string expected)
        {
            await TestExtensionAsync(
                ext =>
                {
                    ext.Url = extension.Url;
                    ext.Value = extension.Value;
                },
                ValidateString,
                expected);
        }
    }
}
