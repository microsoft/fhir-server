// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Search.Converters;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public sealed class ExtensionToReferenceSearchValueConverterTests
    {
        private readonly IReferenceSearchValueParser _referenceSearchValueParser;
        private readonly RequestContextAccessor<IFhirRequestContext> _fhirRequestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();

        public ExtensionToReferenceSearchValueConverterTests()
        {
            var uri = new Uri("https://test:12345");
            _fhirRequestContextAccessor.RequestContext.BaseUri.Returns(uri);
            _referenceSearchValueParser = new ReferenceSearchValueParser(_fhirRequestContextAccessor);
        }

        private async Task<ITypedElementToSearchValueConverter> GetTypeConverterAsync()
        {
            FhirTypedElementToSearchValueConverterManager fhirTypedElementToSearchValueConverterManager = await SearchParameterFixtureData.GetFhirTypedElementToSearchValueConverterManagerAsync();
            fhirTypedElementToSearchValueConverterManager.TryGetConverter("Extension", typeof(ReferenceSearchValue), out ITypedElementToSearchValueConverter extensionConverter);

            return extensionConverter;
        }

        [Fact]
        public async Task GivenAReferenceExtension_WhenConverted_ThenAReferenceSearchValueShouldBeCreated()
        {
            var reference = new ResourceReference("Patient/123");
            var extension = new Extension("test", reference);

            ReferenceSearchValue expected = _referenceSearchValueParser.Parse(reference.Reference);

            var values = (await GetTypeConverterAsync()).ConvertTo(extension.ToTypedElement()).ToList();

            Assert.NotNull(values);
            Assert.True(values.Count == 1);

            var actual = (ReferenceSearchValue)values[0];

            Assert.Equal(expected.Kind, actual.Kind);
            Assert.Equal(expected.BaseUri, actual.BaseUri);
            Assert.Equal(expected.ResourceType, actual.ResourceType);
            Assert.Equal(expected.ResourceId, actual.ResourceId);
        }
    }
}
