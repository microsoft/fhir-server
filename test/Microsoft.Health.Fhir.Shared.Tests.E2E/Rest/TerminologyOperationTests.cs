// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    [Trait(Traits.Category, Categories.Terminology)]
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.Json)]
    public class TerminologyOperationTests : IClassFixture<ValidateTestFixture>
    {
        private const string Success = "All OK";
        private readonly TestFhirClient _client;
        private readonly InProcTestFhirServer _fhirServer;

        public TerminologyOperationTests(ValidateTestFixture fixture)
        {
            _client = fixture.TestFhirClient;
            _fhirServer = (InProcTestFhirServer)fixture.TestFhirServer;
        }

        // Many of these tests may fail as CodeSystem resource definition changed STU3 -> R4 and the TS is on R4

        // $lookup POST tests start
        [SkippableFact]
        public async void GivenValidParamaterInputKnownCode_LookUpReturnTrue()
        {
            var fhirSource = Samples.GetJson("Parameter-LookUp-Correct");
            Parameters resultParam = await _client.LookUpPOSTdAsync("CodeSystem/$lookup", fhirSource);
            foreach (var paramComponenet in resultParam)
            {
                if (paramComponenet.Key == "result")
                {
                    Assert.Equal("true", paramComponenet.Value.ToString());
                }

                break;
            }
        }

        [SkippableFact]
        public async void GivenValidParamaterInputUnknownCode_LookUpThrowsException()
        {
            var fhirSource = Samples.GetJson("Parameter-LookUp-Incorrect");
            await Assert.ThrowsAsync<FhirException>(async () => await _client.LookUpPOSTdAsync("CodeSystem/$lookup", fhirSource));
        }

        [SkippableTheory]
        [InlineData("UIUC#1")]
        [InlineData("")]
        [InlineData("    ")]
        public async void GivenInValidParamaterInput_LookUpThrowsBadRequest(string body)
        {
            await Assert.ThrowsAsync<FhirException>(async () => await _client.LookUpPOSTdAsync("CodeSystem/lookup", body));
        }

        // $lookup POST tests end

        // $lookup GET tests start
        [SkippableTheory]
        [InlineData("CodeSystem/$lookup", "http://hl7.org/fhir/CodeSystem/example", "chol-mass")]
        [InlineData("CodeSystem/$lookup", "http://hl7.org/fhir/CodeSystem/example", "chol-mmol")]
        public async void GivenLookUpForKnownCode_LookUpReturnTrue(string path, string system, string code)
        {
            Parameters resultParam = await _client.LookUpGETdAsync(path, system, code);
            foreach (var paramComponenet in resultParam)
            {
                if (paramComponenet.Key == "result")
                {
                    Assert.True(paramComponenet.Value.ToString() == "true");
                }

                break;
            }
        }

        [SkippableTheory]
        [InlineData("CodeSystem/$lookup", "http://hl7.org/fhir/CodeSystem/example", "drosophola")]
        [InlineData("CodeSystem/$lookup", "http://hl7.org/fhir/CodeSystem/example", "TAATA-box")]
        public async void GivenLookUpForUnknownCode_ThenThrowException(string path, string system, string code)
        {
            await Assert.ThrowsAsync<FhirException>(async () => await _client.LookUpGETdAsync(path, system, code));
        }

        [SkippableTheory]
        [InlineData("CodeSystem/$lookup", "http://hl7.org/fhir/CodeSystem/example", "")]
        [InlineData("CodeSystem/$lookup", "", "drosophola")]
        public async void GivenInvalidRequestParams_LookUpThrowsExcept(string path, string system, string code)
        {
            await Assert.ThrowsAsync<FhirException>(async () => await _client.LookUpGETdAsync(path, system, code));
        }

        // $lookup GET tests end

        // $expand GET tests Start
        [SkippableTheory]
        [InlineData("ValueSet/birthsex/$expand")]
        [InlineData("ValueSet/us-core-narrative-status/$expand")]
        public async void GivenExpandOnValidValueSet_ExpandedValueSetReturned(string path)
        {
            ValueSet resultValueSet = await _client.ExpandGETAsync(path);
            Assert.True(resultValueSet.Expansion != null);
        }

        [SkippableTheory]
        [InlineData("ValueSet/birthsex/$expand?count=0")]
        [InlineData("ValueSet/birthsex/$expand?offset=0")]
        public async void GivenExpandOnValidValueSetEmptyParameters_ExpandedValueSetReturned(string path)
        {
            ValueSet resultValueSet = await _client.ExpandGETAsync(path);
            Assert.True(resultValueSet.Expansion != null);
        }

        [SkippableTheory]
        [InlineData("ValueSet/birthsex/$expand?offset=1", 2)]
        [InlineData("ValueSet/birthsex/$expand?offset=2", 3)]
        [InlineData("ValueSet/birthsex/$expand?offset=3", 4)]
        public async void GivenExpandOnValidValueSetWithOffsetParameter_CorrectOffsetExpandedValueSetReturned(string path, int offset)
        {
            int codeCount = 5;

            ValueSet resultValueSet = await _client.ExpandGETAsync(path);
            Assert.Equal(codeCount - offset, resultValueSet.Expansion.Contains.Count);
        }

        [SkippableTheory]
        [InlineData("ValueSet/birthsex/$expand?count=1", 1)]
        [InlineData("ValueSet/birthsex/$expand?count=2", 2)]
        [InlineData("ValueSet/birthsex/$expand?count=3", 3)]
        public async void GivenExpandOnValidValueSetWithCountParameter_CorrectCountExpandedValueSetReturned(string path, int count)
        {
            ValueSet resultValueSet = await _client.ExpandGETAsync(path);
            Assert.Equal(count, resultValueSet.Expansion.Contains.Count);
        }

        // $expand GET tests end

        // $expand POST tests Start

        [SkippableFact]
        public async void GivenValidParamaterInput_ReturnExpandedValueSet()
        {
            var fhirSource = Samples.GetJson("Parameter-Expand-Correct");
            ValueSet resultValueSet = await _client.ExpandPOSTAsync("ValueSet/$expand", fhirSource);
            Assert.True(resultValueSet.Expansion != null);
        }

        [SkippableFact] // $lookup on a code not in a code system throws...
        public async void GivenValidParamaterInput_LookUpThrows()
        {
            var fhirSource = Samples.GetJson("Parameter-Expand-Incorrect");
            await Assert.ThrowsAsync<FhirException>(async () => await _client.ExpandPOSTAsync("ValueSet/$expand", fhirSource));
        }

        // $expand POST tests end
    }
}
