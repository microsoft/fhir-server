// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
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
    public sealed class ExtensionToDateTimeSearchValueConverterTests : FhirInstanceToSearchValueConverterTests<Extension>
    {
        protected override async Task<ITypedElementToSearchValueConverter> GetTypeConverterAsync()
        {
            FhirTypedElementToSearchValueConverterManager fhirTypedElementToSearchValueConverterManager = await SearchParameterFixtureData.GetFhirTypedElementToSearchValueConverterManagerAsync();
            fhirTypedElementToSearchValueConverterManager.TryGetConverter("Extension", typeof(DateTimeSearchValue), out ITypedElementToSearchValueConverter extensionConverter);

            return extensionConverter;
        }

        public static IEnumerable<object[]> GetDateTimeExtensionDataSource()
        {
            yield return new object[] { new Extension("test", new Period { Start = "2017" }), "2017", "9999-12-31T23:59:59" };
            yield return new object[] { new Extension("test", new FhirDateTime { Value = "2018-03-30T05:12" }), "2018-03-30T05:12" };
            yield return new object[] { new Extension("test", new Date { Value = "2018-03-30T05:12" }), "2018-03-30T05:12" };
            yield return new object[] { new Extension("test", new Instant { Value = new DateTimeOffset(2018, 01, 20, 14, 34, 24, TimeSpan.FromMinutes(60)) }), "2018-01-20T13:34:24.0000000-00:00" };
        }

        [Theory]
        [MemberData(nameof(GetDateTimeExtensionDataSource))]
        public async Task GivenADateTimeExtension_WhenConverted_ThenADateTimeSearchValueShouldBeCreated(Extension extension, string start, string end = null)
        {
            await TestExtensionAsync(
                ext =>
                {
                    ext.Url = extension.Url;
                    ext.Value = extension.Value;
                },
                ValidateDateTime,
                (start, end ?? start));
        }
    }
}
