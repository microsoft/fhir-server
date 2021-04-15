// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using System.Net;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    [Trait(Traits.Category, Categories.MemberMatch)]
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.All)]
    public sealed class MemberMatchTests : IClassFixture<MemberMatchTestFixture>
    {
        private readonly FhirClient _client;

        private readonly MemberMatchTestFixture _fixture;

        public MemberMatchTests(MemberMatchTestFixture fixture)
        {
            _fixture = fixture;
            _client = fixture.TestFhirClient;
        }

        [Theory]
        [InlineData("Seattle", null, null, FinancialResourceStatusCodes.Active, "P0", "01")]
        [InlineData(null, null, "1970", FinancialResourceStatusCodes.Cancelled, null, "02")]
        [InlineData(null, null, "1970-02", FinancialResourceStatusCodes.Active, null, "03")]
        [InlineData(null, null, "1970", FinancialResourceStatusCodes.Active, "P8", "01")]
        [InlineData(null, "Williamas", null, FinancialResourceStatusCodes.Active, "P2", "02")]
        [InlineData(null, "Robinson", null, FinancialResourceStatusCodes.Active, null, "02")]
        public async Task GivenUniqueInformation_WhenMemberMatchSent_ThenPatientFound(string city, string name, string date, FinancialResourceStatusCodes status, string subPlan, string expectedId)
        {
            var searchPatient = new Patient();
            _fixture.SetPatient(searchPatient, city, name, birthDate: date);
            var searchCoverage = new Coverage();
            _fixture.SetCoverage(searchCoverage, searchPatient, status, subPlan);
            var outParameters = await _client.MemberMatch(searchPatient, searchCoverage);

            var returnPatient = outParameters.Get("MemberPatient").First().Resource as Patient;
            var identifeir = returnPatient.Identifier.Where(x => x.Type.Coding.Where(code => code.Code == "UMB").Any()).First().Value;

            Assert.Equal(expectedId, identifeir);
        }

        [Theory]
        [InlineData("Seattle", null, null, FinancialResourceStatusCodes.Active, null)]
        [InlineData(null, null, "1970", FinancialResourceStatusCodes.Active, null)]


        public async Task GivenNotEnoughInformation_WhenMemberMatchSent_ThenTooManyFound(string city, string name, string date, FinancialResourceStatusCodes status, string subPlan)
        {
            var searchPatient = new Patient();
            _fixture.SetPatient(searchPatient, city, name, birthDate: date);
            var searchCoverage = new Coverage();
            _fixture.SetCoverage(searchCoverage, searchPatient, status, subPlan);
            var ex = await Assert.ThrowsAsync<FhirException>(async () => await _client.MemberMatch(searchPatient, searchCoverage));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, ex.StatusCode);
            Assert.Equal(Core.Resources.MemberMatchMultipleMatchesFound, ex.OperationOutcome.Issue.First().Diagnostics);
        }

        [Theory]
        [InlineData("Seattle", "Williams", null, FinancialResourceStatusCodes.Active, "P0")]
        [InlineData(null, null, "1970-02", FinancialResourceStatusCodes.Cancelled, null)]
        [InlineData(null, null, "1980", FinancialResourceStatusCodes.Active, "P0")]

        public async Task GivenTooMuchInformation_WhenMemberMatchSent_ThenNothingFound(string city, string name, string date, FinancialResourceStatusCodes status, string subPlan)
        {
            var searchPatient = new Patient();
            _fixture.SetPatient(searchPatient, city, name, birthDate: date);
            var searchCoverage = new Coverage();
            _fixture.SetCoverage(searchCoverage, searchPatient, status, subPlan);
            var ex = await Assert.ThrowsAsync<FhirException>(async () => await _client.MemberMatch(searchPatient, searchCoverage));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, ex.StatusCode);
            Assert.Equal(Core.Resources.MemberMatchNoMatchFound, ex.OperationOutcome.Issue.First().Diagnostics);
        }
    }
}
