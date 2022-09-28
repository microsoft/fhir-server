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
    public sealed class ExtensionToTokenSearchValueConverterTests : FhirInstanceToSearchValueConverterTests<Extension>
    {
        protected override async Task<ITypedElementToSearchValueConverter> GetTypeConverterAsync()
        {
            FhirTypedElementToSearchValueConverterManager fhirTypedElementToSearchValueConverterManager = await SearchParameterFixtureData.GetFhirTypedElementToSearchValueConverterManagerAsync();
            fhirTypedElementToSearchValueConverterManager.TryGetConverter("Extension", typeof(TokenSearchValue), out ITypedElementToSearchValueConverter extensionConverter);

            return extensionConverter;
        }

        public static IEnumerable<object[]> GetTokenExtensionDataSource()
        {
            yield return new object[] { new Extension("test", new Coding("system", "code")), new Token(system: "system", code: "code") };
            yield return new object[] { new Extension("test", new Code("code")), new Token(code: "code") };
            yield return new object[] { new Extension("test", new CodeableConcept("system", null, null)), new Token(system: "system", code: null, text: null) };
            yield return new object[] { new Extension("test", new Id("id")), new Token(code: "id") };
            yield return new object[] { new Extension("test", new Identifier("system", "value")), new Token(system: "system", code: "value") };
            yield return new object[] { new Extension("test", new ContactPoint(ContactPoint.ContactPointSystem.Phone, ContactPoint.ContactPointUse.Home, "value")), new Token(system: "home", "value") };
            yield return new object[] { new Extension("test", new FhirBoolean(true)), new Token(system: "http://hl7.org/fhir/special-values", code: "true") };
            yield return new object[] { new Extension("test", new FhirString("value")), new Token(code: "value") };
        }

        [Theory]
        [MemberData(nameof(GetTokenExtensionDataSource))]
        public async Task GivenATokenExtension_WhenConverted_ThenATokenSearchValueShouldBeCreated(Extension extension, Token expected)
        {
            await TestExtensionAsync(
                ext =>
                {
                    ext.Url = extension.Url;
                    ext.Value = extension.Value;
                },
                ValidateToken,
                expected);
        }
    }
}
