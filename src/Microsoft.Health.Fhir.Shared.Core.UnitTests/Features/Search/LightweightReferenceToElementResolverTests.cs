// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using Hl7.Fhir.FhirPath;
using Hl7.Fhir.Model;
using Hl7.FhirPath;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search
{
    public class LightweightReferenceToElementResolverTests
    {
        private readonly LightweightReferenceToElementResolver _resolver;
        private readonly Encounter _encounter;
        private readonly FhirEvaluationContext _context;

        #if Stu3 || R4
        private const string FhirPath = "Encounter.participant.individual";
        #else
        private const string FhirPath = "Encounter.participant.actor";
        #endif

        public LightweightReferenceToElementResolverTests()
        {
            ReferenceSearchValueParser referenceSearchValueParser = Mock.TypeWithArguments<ReferenceSearchValueParser>(new FhirRequestContextAccessor());

            _resolver = new LightweightReferenceToElementResolver(referenceSearchValueParser, ModelInfoProvider.Instance);
            _encounter = Samples.GetJsonSample<Encounter>("Encounter-For-Patient-f001");
            _context = new FhirEvaluationContext
            {
                ElementResolver = _resolver.Resolve,
            };
            FhirPathCompiler.DefaultSymbolTable.AddFhirExtensions();
        }

        [InlineData("Patient/1234")]
        [InlineData("Patient/1234/_history/56789")]
        [Theory]
        public void GivenAValidReference_WhenConvertingAReferenceToTypedElement_ThenTheResultIsValid(string patientRef)
        {
            var result = _resolver.Resolve(patientRef);

            Assert.Equal("Patient", result.InstanceType);
            Assert.Equal("1234", result.Children("id").Single().Value);
        }

        [InlineData("Test/1234")]
        [InlineData("Patient")]
        [InlineData("")]
        [InlineData(null)]
        [Theory]
        public void GivenAnInvalidReference_WhenConvertingAReferenceToTypedElement_ThenTheResultIsNull(string patientRef)
        {
            var result = _resolver.Resolve(patientRef);

            Assert.Null(result);
        }

        [Fact]
        public void GivenAnEncounter_WhenResolvingAPractitionerInAFhirPathExpression_ThenTheResultIsValid()
        {
            var result = _encounter
                .Select($"{FhirPath}.where(resolve() is Practitioner)", _context)
                .SingleOrDefault();

            Assert.IsType<ResourceReference>(result);
        }

        [Fact]
        public void GivenAnEncounterWithWrongTypeInResolve_WhenResolvingAPatientInAFhirPathExpression_ThenTheResultIsNull()
        {
            var result = _encounter
                .Select($"{FhirPath}.where(resolve() is Organization)", _context)
                .SingleOrDefault();

            Assert.Null(result);
        }
    }
}
