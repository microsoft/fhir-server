// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.FhirPath;
using Hl7.Fhir.Model;
using Hl7.FhirPath;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Converters;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;
using static Microsoft.Health.Fhir.Tests.Common.Search.SearchValueValidationHelper;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.Converters
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class CodeOfTToTokenSearchValueConverterTests : FhirInstanceToSearchValueConverterTests<Code<ObservationStatus>>
    {
        protected override ITypedElement TypedElement
        {
            get
            {
                var observation = new Observation
                {
                    StatusElement = Element,
                }.ToTypedElement();

                return observation.Select("Observation.status").Single();
            }
        }

        protected override async Task<ITypedElementToSearchValueConverter> GetTypeConverterAsync()
        {
            var resolver = new CodeSystemResolver(ModelInfoProvider.Instance);
            await resolver.StartAsync(CancellationToken.None);
            return new CodeToTokenSearchValueConverter(resolver);
        }

        [Fact]
        public async Task GivenACodeAndSystem_WhenConverted_ThenATokenSearchValueShouldBeCreated()
        {
            await Test(
                code => code.Value = ObservationStatus.Final,
                ValidateToken,
                new Token("http://hl7.org/fhir/observation-status", "final"));
        }

        [Fact]
        public async Task GivenANullCode_WhenConverted_ThenNullValueReturned()
        {
            await Test(
                code => code.Value = null,
                ValidateNull,
                new Code<ResourceType>(null));
        }
    }
}
