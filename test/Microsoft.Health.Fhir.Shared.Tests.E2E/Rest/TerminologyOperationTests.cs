// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.Extensions;
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
        private readonly TestFhirServer _server;

        public TerminologyOperationTests(ValidateTestFixture fixture)
        {
            _client = fixture.TestFhirClient;
            _server = fixture.TestFhirServer;
        }

        // Many of these tests may fail as CodeSystem resource definition changed STU3 -> R4 and the TS is on R4

        // $lookup POST tests start
        [SkippableFact]
        public async void GivenValidParamaterInput_WhenKnownCode_ThenReturnTrue()
        {
            Skip.If(!_server.Metadata.SupportsTerminologyOperation("lookup"));
            var fhirSource = Samples.GetJson("Parameter-LookUp-Correct");
            Parameters resultParam = await _client.LookUpPOSTAsync("CodeSystem/$lookup", fhirSource);
            bool passed = false;
            foreach (var paramComponenet in resultParam.Parameter)
            {
                if (paramComponenet.Name == "name")
                {
                    passed = true;
                    break;
                }
            }

            Assert.True(passed);
        }

        [SkippableFact]
        public async void GivenLookUpValidParamaterInput_WhenUnknownCode_ThenThrowsException()
        {
            Skip.If(!_server.Metadata.SupportsTerminologyOperation("lookup"));
            var fhirSource = Samples.GetJson("Parameter-LookUp-Incorrect");
            await Assert.ThrowsAsync<FhirException>(async () => await _client.LookUpPOSTAsync("CodeSystem/$lookup", fhirSource));
        }

        [SkippableTheory]
        [InlineData("UIUC#1")]
        [InlineData("")]
        [InlineData("    ")]
        public async void GivenLookUp_WhenInValidParamaterInput_ThenThrowsBadRequest(string body)
        {
            Skip.If(!_server.Metadata.SupportsTerminologyOperation("lookup"));
            await Assert.ThrowsAsync<FhirException>(async () => await _client.LookUpPOSTAsync("CodeSystem/lookup", body));
        }

        // $lookup POST tests end

        // $lookup GET tests start
        [SkippableTheory]
        [InlineData("CodeSystem/$lookup", "http://hl7.org/fhir/CodeSystem/example", "chol-mass")]
        [InlineData("CodeSystem/$lookup", "http://hl7.org/fhir/CodeSystem/example", "chol-mmol")]
        public async void GivenLookUp_WhenKnownCode_ThenReturnTrue(string path, string system, string code)
        {
            Skip.If(!_server.Metadata.SupportsTerminologyOperation("lookup"));
            Parameters resultParam = await _client.LookUpGETAsync(path, system, code);
            bool passed = false;
            foreach (var paramComponenet in resultParam.Parameter)
            {
                if (paramComponenet.Name == "name")
                {
                    passed = true;
                    break;
                }
            }

            Assert.True(passed);
        }

        [SkippableTheory]
        [InlineData("CodeSystem/$lookup", "http://hl7.org/fhir/CodeSystem/example", "drosophola")]
        [InlineData("CodeSystem/$lookup", "http://hl7.org/fhir/CodeSystem/example", "TAATA-box")]
        public async void GivenLookUp_WhenUnknownCode_ThenThrowException(string path, string system, string code)
        {
            Skip.If(!_server.Metadata.SupportsTerminologyOperation("lookup"));
            await Assert.ThrowsAsync<FhirException>(async () => await _client.LookUpGETAsync(path, system, code));
        }

        [SkippableTheory]
        [InlineData("CodeSystem/$lookup", "http://hl7.org/fhir/CodeSystem/example", "")]
        [InlineData("CodeSystem/$lookup", "", "drosophola")]
        public async void GivenLookUp_WhenInvalidRequestParams_ThenThrowsExcept(string path, string system, string code)
        {
            Skip.If(!_server.Metadata.SupportsTerminologyOperation("lookup"));
            await Assert.ThrowsAsync<FhirException>(async () => await _client.LookUpGETAsync(path, system, code));
        }

        // $lookup GET tests end

        // $expand GET tests Start
        [SkippableTheory]
        [InlineData("ValueSet/birthsex/$expand")]
        [InlineData("ValueSet/us-core-narrative-status/$expand")]
        public async void GivenExpand_WhenValidValueSet_ThenExpandedValueSetReturned(string path)
        {
            Skip.If(!_server.Metadata.SupportsTerminologyOperation("expand"));
            ValueSet resultValueSet = await _client.ExpandGETAsync(path);
            Assert.True(resultValueSet.Expansion != null);
        }

        [SkippableTheory]
        [InlineData("ValueSet/birthsex/$expand?count=0")]
        [InlineData("ValueSet/birthsex/$expand?offset=0")]
        public async void GivenExpand_WhenValidValueSetEmptyParameters_ThenExpandedValueSetReturned(string path)
        {
            Skip.If(!_server.Metadata.SupportsTerminologyOperation("expand"));
            ValueSet resultValueSet = await _client.ExpandGETAsync(path);
            Assert.True(resultValueSet.Expansion != null);
        }

        [SkippableTheory]
        [InlineData("ValueSet/birthsex/$expand?offset=1", 1)]
        [InlineData("ValueSet/birthsex/$expand?offset=2", 2)]
        [InlineData("ValueSet/birthsex/$expand?offset=3", 3)]
        public async void GivenExpand_WhenValidValueSetWithOffsetParameter_ThenCorrectOffsetExpandedValueSetReturned(string path, int offset)
        {
            Skip.If(!_server.Metadata.SupportsTerminologyOperation("expand"));
            int codeCount = 3;

            ValueSet resultValueSet = await _client.ExpandGETAsync(path);
            Assert.Equal(codeCount - offset, resultValueSet.Expansion.Contains.Count);
        }

        [SkippableTheory]
        [InlineData("ValueSet/birthsex/$expand?count=1", 1)]
        [InlineData("ValueSet/birthsex/$expand?count=2", 2)]
        [InlineData("ValueSet/birthsex/$expand?count=3", 3)]
        public async void GivenExpand_WhenValidValueSetWithCountParameter_ThenCorrectCountExpandedValueSetReturned(string path, int count)
        {
            Skip.If(!_server.Metadata.SupportsTerminologyOperation("expand"));
            ValueSet resultValueSet = await _client.ExpandGETAsync(path);
            Assert.Equal(count, resultValueSet.Expansion.Contains.Count);
        }

        // $expand GET tests end

        // $expand POST tests Start

        [SkippableFact]
        public async void GivenExpand_WhenValidParamaterInput_ThenReturnExpandedValueSet()
        {
            Skip.If(!_server.Metadata.SupportsTerminologyOperation("expand"));
            var fhirSource = Samples.GetJson("Parameter-Expand-Correct");
            ValueSet resultValueSet = await _client.ExpandPOSTAsync("ValueSet/$expand", fhirSource);
            Assert.True(resultValueSet.Expansion != null);
        }

        [SkippableFact] // $lookup on a code not in a code system throws...
        public async void GivenExpand_WhenValidParamaterInput_ThenExpandThrows()
        {
            Skip.If(!_server.Metadata.SupportsTerminologyOperation("expand"));
            var fhirSource = Samples.GetJson("Parameter-Expand-Incorrect");
            await Assert.ThrowsAsync<FhirException>(async () => await _client.ExpandPOSTAsync("ValueSet/$expand", fhirSource));
        }

        // $expand POST tests end
    }
}
