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
    [Trait(Traits.Category, Categories.Validate)]
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

        // Many of these tests may fail as CodeSystem resource changed STU3 -> R4 and the TS is on R4

        // $lookup POST tests start
        [SkippableFact]
        public async void GivenValidParamaterInput_LookUpReturnTrue()
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

        [SkippableFact] // $lookup on a code not in a code system throws...
        public async void GivenValidParamaterInput_LookUpReturnFalse()
        {
            var fhirSource = Samples.GetJson("Parameter-LookUp-Incorrect");
            await Assert.ThrowsAsync<FhirException>(async () => await _client.LookUpPOSTdAsync("CodeSystem/$lookup", fhirSource));
        }

        [SkippableTheory]
        [InlineData("UIUC#1")]
        [InlineData("")]
        [InlineData("    ")]
        public async void GivenInValidParamaterInput_ValidateCodeThrowsBadRequest(string body)
        {
            await Assert.ThrowsAsync<FhirException>(async () => await _client.LookUpPOSTdAsync("CodeSystem/lookup", body));
        }

        // $lookup POST tests end

        // $lookup GET tests start
        [SkippableTheory]
        [InlineData("CodeSystem/$lookup", "http://hl7.org/fhir/CodeSystem/example", "chol-mass")]
        [InlineData("CodeSystem/$lookup", "http://hl7.org/fhir/CodeSystem/example", "chol-mmol")]
        public async void GivenLookUpForKnownCode_ThenTrueParameterIsReturned(string path, string system, string code)
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
        public async void GivenLookUpForUnknownCode_ThenFalseParameterIsReturned(string path, string system, string code)
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
    }
}
