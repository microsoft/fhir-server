// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

// using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Hl7.Fhir.Model;

// using Hl7.Fhir.Rest;
using Hl7.Fhir.Serialization;

// using Microsoft.Azure.Cosmos.Core;
using Microsoft.Health.Core.Extensions;
using Microsoft.Health.Fhir.Client;

// using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Xunit.Abstractions;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    [CollectionDefinition(Categories.CustomSearch, DisableParallelization = true)]
    [Collection(Categories.CustomSearch)]
    [Trait(Traits.Category, Categories.CustomSearch)]
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.Json)]
    public class TokenOverflowTests : SearchTestsBase<HttpIntegrationTestFixture>, IAsyncLifetime
    {
        private readonly HttpIntegrationTestFixture _fixture;
        private ITestOutputHelper _output;

        public TokenOverflowTests(HttpIntegrationTestFixture fixture, ITestOutputHelper output)
            : base(fixture)
        {
            _fixture = fixture;
            _output = output;
        }

        private delegate string GetOneParameter(Patient patient);

        public async Task InitializeAsync()
        {
            // await Client.DeleteAllResources(ResourceType.Patient, null);

            await Client.DeleteAllResources(ResourceType.Specimen, null);
            await Client.DeleteAllResources(ResourceType.Immunization, null);
        }

        private string GetTokenValue(string prefix, string suffix = null)
        {
            int noOverflowMaxLength;
            checked
            {
                noOverflowMaxLength = (int)VLatest.TokenSearchParam.Code.Metadata.MaxLength;
            }

            if (prefix.Length > noOverflowMaxLength)
            {
                throw new Exception("Token prefix too long.");
            }

            return prefix.PadRight(noOverflowMaxLength, '-') + suffix;
        }

        private void EnsureSuccessStatusCode(HttpStatusCode httpStatusCode, string message)
        {
            int code = (int)httpStatusCode;
            if (code < 200 || code > 299)
            {
                throw new Exception($"{message} Failed, returned http code is {httpStatusCode}, {code}.");
            }
        }

        private void LoadTestPatients(string name, out Patient patientAWithTokenOverflow, out Patient patientBWithTokenOverflow, out Patient patientCWithNoTokenOverflow)
        {
            Patient patient = Samples.GetJsonSample<Patient>("PatientTokenOverflow");

            patientAWithTokenOverflow = (Patient)patient.DeepCopy();
            patientAWithTokenOverflow.Id = $"{name}-A-test-patient-with-token-overflow";
            patientAWithTokenOverflow.Name[0].Family = $"{name}-A";
            patientAWithTokenOverflow.BirthDate = "2016-01-15";
            patientAWithTokenOverflow.Identifier[0].Value = GetTokenValue(name, "A");
            patientAWithTokenOverflow.Telecom[0].Value = "555-555-5555";
            patientAWithTokenOverflow.ManagingOrganization.Reference = "Organization-A";

            patientBWithTokenOverflow = (Patient)patient.DeepCopy();
            patientBWithTokenOverflow.Id = $"{name}-B-test-patient-with-token-overflow";
            patientBWithTokenOverflow.Name[0].Family = $"{name}-B";
            patientBWithTokenOverflow.BirthDate = "2016-01-16";
            patientBWithTokenOverflow.Identifier[0].Value = GetTokenValue(name, "B");
            patientBWithTokenOverflow.Telecom[0].Value = "555-555-5556";
            patientBWithTokenOverflow.ManagingOrganization.Reference = "Organization-B";

            patientCWithNoTokenOverflow = (Patient)patient.DeepCopy();
            patientCWithNoTokenOverflow.Id = $"{name}-C-test-patient-with-no-token-overflow";
            patientCWithNoTokenOverflow.Name[0].Family = $"{name}-C";
            patientCWithNoTokenOverflow.BirthDate = "2016-01-17";
            patientCWithNoTokenOverflow.Identifier[0].Value = GetTokenValue(name);
            patientCWithNoTokenOverflow.Telecom[0].Value = "555-555-5557";
            patientCWithNoTokenOverflow.ManagingOrganization.Reference = "Organization-C";
        }

        [SkippableFact]
        public async Task GivenResourcesWithAndWithoutTokenOverflow_WhenSearchByToken_VerifyCorrectSerachResults()
        {
            try
            {
                string name = "Kirk-T-" + Guid.NewGuid().ToString().ComputeHash().Substring(0, 14).ToLower();

                LoadTestPatients(name, out Patient patientAWithTokenOverflow, out Patient patientBWithTokenOverflow, out Patient patientCWithNoTokenOverflow);

                // Create patients.

                // POST patient A.
                FhirResponse<Patient> createdPatientA = await Client.CreateAsync(patientAWithTokenOverflow);
                EnsureSuccessStatusCode(createdPatientA.StatusCode, "Creating patient A.");

                // POST patient B.
                FhirResponse<Patient> createdPatientB = await Client.CreateAsync(patientBWithTokenOverflow);
                EnsureSuccessStatusCode(createdPatientA.StatusCode, "Creating patient B.");

                // POST patient C.
                FhirResponse<Patient> createdPatientC = await Client.CreateAsync(patientCWithNoTokenOverflow);
                EnsureSuccessStatusCode(createdPatientA.StatusCode, "Creating patient C.");

                // Verify we can search patients correctly.

                await ExecuteAndValidateBundle(
                    $"Patient?identifier={patientAWithTokenOverflow.Identifier[0].Value}",
                    createdPatientA);

                await ExecuteAndValidateBundle(
                    $"Patient?identifier={patientBWithTokenOverflow.Identifier[0].Value}",
                    createdPatientB);

                await ExecuteAndValidateBundle(
                    $"Patient?identifier={patientCWithNoTokenOverflow.Identifier[0].Value}",
                    createdPatientC);

                await ExecuteAndValidateBundle("Patient?identifier=nonexistant-patient");
            }
            catch (Exception e)
            {
                _output.WriteLine($"Exception: {e.Message}");
                _output.WriteLine($"Stack Trace: {e.StackTrace}");
                throw;
            }
        }

        /*
        [SkippableFact]
        public async Task GivenResourcesWithAndWithoutTokenOverflow_WhenSearchByTokenString_VerifyCorrectSerachResults()
        {
            try
            {
                string id = "KirkCTS" + Guid.NewGuid().ToString().ComputeHash().Substring(0, 14).ToLower();

                SearchParameter searchParam = Samples.GetJsonSample<SearchParameter>("CompositeCustomTokenStringSearchParameter");
                LoadTestPatients(id, out Patient patientAWithTokenOverflow, out Patient patientBWithTokenOverflow, out Patient patientCWithNoTokenOverflow);

                // POST patient A.
                FhirResponse<Patient> createdPatientA = await Client.CreateAsync(patientAWithTokenOverflow);
                EnsureSuccessStatusCode(createdPatientA.StatusCode, "Creating patient A.");

                // POST custom composite search parameter.
                FhirResponse<SearchParameter> createdSearchParam = await Client.CreateAsync(searchParam);
                EnsureSuccessStatusCode(createdPatientA.StatusCode, "Creating custom composite search parameter.");

                // POST patient B.
                FhirResponse<Patient> createdPatientB = await Client.CreateAsync(patientBWithTokenOverflow);
                EnsureSuccessStatusCode(createdPatientA.StatusCode, "Creating patient B.");

                // POST patient C.
                FhirResponse<Patient> createdPatientC = await Client.CreateAsync(patientCWithNoTokenOverflow);
                EnsureSuccessStatusCode(createdPatientA.StatusCode, "Creating patient C.");

                // Without x-ms-use-partial-indices header we cannot search for resources created after the search parameter was created.
                Bundle bundle = await Client.SearchAsync($"Patient?identifier-name-family={patientBWithTokenOverflow.Identifier[0].Value}${patientBWithTokenOverflow.Name[0].Family}");
                OperationOutcome operationOutcome = GetAndValidateOperationOutcome(bundle);
                string[] expectedDiagnostics = { string.Format(Core.Resources.SearchParameterNotSupported, "identifier-name-family", "Patient") };
                OperationOutcome.IssueSeverity[] expectedIssueSeverities = { OperationOutcome.IssueSeverity.Warning };
                OperationOutcome.IssueType[] expectedCodeTypes = { OperationOutcome.IssueType.NotSupported };
                ValidateOperationOutcome(expectedDiagnostics, expectedIssueSeverities, expectedCodeTypes, operationOutcome);

                // With x-ms-use-partial-indices header we can search only for resources created after the search parameter was created.

                await ExecuteAndValidateBundle(
                    $"Patient?identifier-name-family={patientAWithTokenOverflow.Identifier[0].Value}${patientAWithTokenOverflow.Name[0].Family}",
                    false,
                    false,
                    new Tuple<string, string>("x-ms-use-partial-indices", "true"));

                await ExecuteAndValidateBundle(
                    $"Patient?identifier-name-family={patientBWithTokenOverflow.Identifier[0].Value}${patientBWithTokenOverflow.Name[0].Family}",
                    false,
                    false,
                    new Tuple<string, string>("x-ms-use-partial-indices", "true"),
                    createdPatientB);

                await ExecuteAndValidateBundle(
                    $"Patient?identifier-name-family={patientCWithNoTokenOverflow.Identifier[0].Value}${patientCWithNoTokenOverflow.Name[0].Family}",
                    false,
                    false,
                    new Tuple<string, string>("x-ms-use-partial-indices", "true"),
                    createdPatientC);

                // Reindex DB.

                Uri reindexJobUri;

                // Start a reindex job
                (_, reindexJobUri) = await Client.PostReindexJobAsync(new Parameters());

                await WaitForReindexStatus(reindexJobUri, "Completed");

                FhirResponse<Parameters> reindexJobResult = await Client.CheckReindexAsync(reindexJobUri);
                Parameters.ParameterComponent param = reindexJobResult.Resource.Parameter.FirstOrDefault(p => p.Name == "searchParams");
                _output.WriteLine("ReindexJobDocument:");
                var serializer = new FhirJsonSerializer();
                _output.WriteLine(serializer.SerializeToString(reindexJobResult.Resource));

                Assert.Contains(createdSearchParam.Resource.Url, param?.Value?.ToString());

                reindexJobResult = await WaitForReindexStatus(reindexJobUri, "Completed");
                _output.WriteLine($"Reindex job is completed, it should have reindexed the resources with {id}.");

                bool floatParse = float.TryParse(
                    reindexJobResult.Resource.Parameter.FirstOrDefault(predicate => predicate.Name == "resourcesSuccessfullyReindexed").Value.ToString(),
                    out float resourcesReindexed);

                _output.WriteLine($"Reindex job is completed, {resourcesReindexed} resources Reindexed.");

                Assert.True(floatParse);
                Assert.True(resourcesReindexed > 0.0);

                // After reindexing no need to use x-ms-use-partial-indices, all resources are searchable.
                await ExecuteAndValidateBundle(
                    $"Patient?identifier-name-family={patientAWithTokenOverflow.Identifier[0].Value}${patientAWithTokenOverflow.Name[0].Family}",
                    createdPatientA);

                await ExecuteAndValidateBundle(
                    $"Patient?identifier-name-family={patientBWithTokenOverflow.Identifier[0].Value}${patientBWithTokenOverflow.Name[0].Family}",
                    createdPatientB);

                await ExecuteAndValidateBundle(
                    $"Patient?identifier-name-family={patientCWithNoTokenOverflow.Identifier[0].Value}${patientCWithNoTokenOverflow.Name[0].Family}",
                    createdPatientC);

                // Invlid composite search parameter returns nothing (we combine patients A and B).
                await ExecuteAndValidateBundle(
                    $"Patient?identifier-name-family={patientAWithTokenOverflow.Identifier[0].Value}${patientBWithTokenOverflow.Name[0].Family}");
            }
            catch (Exception e)
            {
                _output.WriteLine($"Exception: {e.Message}");
                _output.WriteLine($"Stack Trace: {e.StackTrace}");
                throw;
            }
        }
        */

        [SkippableFact]
        public async Task AAAA_GivenResourcesWithAndWithoutTokenOverflow_WhenSearchByTokenString_VerifyCorrectSerachResults()
        {
            await TestCompositeTokenOverflow(
                "Kirk-CTS",
                "CompositeCustomTokenStringSearchParameter",
                "Patient-identifier-individual-family",
                patient => patient.Identifier[0].Value,
                patient => patient.Name[0].Family);
        }

        [SkippableFact]
        public async Task BBBB_GivenResourcesWithAndWithoutTokenOverflow_WhenSearchByTokenString_VerifyCorrectSerachResults()
        {
            await TestCompositeTokenOverflow(
                "Kirk-CTD",
                "CompositeCustomTokenDateTimeSearchParameter",
                "Patient-identifier-individual-birthdate",
                patient => patient.Identifier[0].Value,
                patient => patient.BirthDate);
        }

        [SkippableFact]
        public async Task CCCC_GivenResourcesWithAndWithoutTokenOverflow_WhenSearchByTokenString_VerifyCorrectSerachResults()
        {
            await TestCompositeTokenOverflow(
                "Kirk-CToT",
                "CompositeCustomTokenOverflowTokenSearchParameter",
                "Patient-identifier-individual-phone",
                patient => patient.Identifier[0].Value,
                patient => patient.Telecom[0].Value);
        }

        [SkippableFact]
        public async Task DDDD_GivenResourcesWithAndWithoutTokenOverflow_WhenSearchByTokenString_VerifyCorrectSerachResults()
        {
            await TestCompositeTokenOverflow(
                "Kirk-CTTo",
                "CompositeCustomTokenTokenOverflowSearchParameter",
                "individual-phone-Patient-identifier",
                patient => patient.Telecom[0].Value,
                patient => patient.Identifier[0].Value);
        }

        [SkippableFact]
        public async Task EEEE_GivenResourcesWithAndWithoutTokenOverflow_WhenSearchByTokenString_VerifyCorrectSerachResults()
        {
            await TestCompositeTokenOverflow(
                "Kirk-CRT",
                "CompositeCustomReferenceTokenSearchParameter",
                "Patient-organization-Patient-identifier",
                patient => patient.ManagingOrganization.Reference,
                patient => patient.Identifier[0].Value);
        }

        private async Task TestCompositeTokenOverflow(
            string resourceNamePrefix,
            string searchParameterTestFileName,
            string searchParameterName,
            GetOneParameter getParameter1,
            GetOneParameter getParameter2)
        {
            try
            {
                string rnd = Guid.NewGuid().ToString().ComputeHash().Substring(0, 14).ToLower();
                string name = $"{resourceNamePrefix}-{rnd}";
                searchParameterName = $"{searchParameterName}-{name}";

                SearchParameter searchParam = Samples.GetJsonSample<SearchParameter>(searchParameterTestFileName);
                searchParam.Name = searchParameterName;
                searchParam.Url = "http://hl7.org/fhir/SearchParameter/" + searchParameterName;
                searchParam.Code = searchParameterName;

                LoadTestPatients(name, out Patient patientAWithTokenOverflow, out Patient patientBWithTokenOverflow, out Patient patientCWithNoTokenOverflow);

                // POST patient A.
                FhirResponse<Patient> createdPatientA = await Client.CreateAsync(patientAWithTokenOverflow);
                EnsureSuccessStatusCode(createdPatientA.StatusCode, "Creating patient A.");

                // POST custom composite search parameter.
                FhirResponse<SearchParameter> createdSearchParam = await Client.CreateAsync(searchParam);
                EnsureSuccessStatusCode(createdPatientA.StatusCode, "Creating custom composite search parameter.");

                // POST patient B.
                FhirResponse<Patient> createdPatientB = await Client.CreateAsync(patientBWithTokenOverflow);
                EnsureSuccessStatusCode(createdPatientA.StatusCode, "Creating patient B.");

                // POST patient C.
                FhirResponse<Patient> createdPatientC = await Client.CreateAsync(patientCWithNoTokenOverflow);
                EnsureSuccessStatusCode(createdPatientA.StatusCode, "Creating patient C.");

                // Without x-ms-use-partial-indices header we cannot search for resources created after the search parameter was created.
                Bundle bundle = await Client.SearchAsync($"Patient?{searchParameterName}={getParameter1(patientBWithTokenOverflow)}${getParameter2(patientBWithTokenOverflow)}");
                OperationOutcome operationOutcome = GetAndValidateOperationOutcome(bundle);
                string[] expectedDiagnostics = { string.Format(Core.Resources.SearchParameterNotSupported, searchParameterName, "Patient") };
                OperationOutcome.IssueSeverity[] expectedIssueSeverities = { OperationOutcome.IssueSeverity.Warning };
                OperationOutcome.IssueType[] expectedCodeTypes = { OperationOutcome.IssueType.NotSupported };
                ValidateOperationOutcome(expectedDiagnostics, expectedIssueSeverities, expectedCodeTypes, operationOutcome);

                // With x-ms-use-partial-indices header we can search only for resources created after the search parameter was created.

                await ExecuteAndValidateBundle(
                    $"Patient?{searchParameterName}={getParameter1(patientAWithTokenOverflow)}${getParameter2(patientAWithTokenOverflow)}",
                    false,
                    false,
                    new Tuple<string, string>("x-ms-use-partial-indices", "true"));

                await ExecuteAndValidateBundle(
                    $"Patient?{searchParameterName}={getParameter1(patientBWithTokenOverflow)}${getParameter2(patientBWithTokenOverflow)}",
                    false,
                    false,
                    new Tuple<string, string>("x-ms-use-partial-indices", "true"),
                    createdPatientB);

                await ExecuteAndValidateBundle(
                    $"Patient?{searchParameterName}={getParameter1(patientCWithNoTokenOverflow)}${getParameter2(patientCWithNoTokenOverflow)}",
                    false,
                    false,
                    new Tuple<string, string>("x-ms-use-partial-indices", "true"),
                    createdPatientC);

                // Reindex DB.

                Uri reindexJobUri;

                // Start a reindex job
                (_, reindexJobUri) = await Client.PostReindexJobAsync(new Parameters());

                await WaitForReindexStatus(reindexJobUri, "Completed");

                FhirResponse<Parameters> reindexJobResult = await Client.CheckReindexAsync(reindexJobUri);
                Parameters.ParameterComponent param = reindexJobResult.Resource.Parameter.FirstOrDefault(p => p.Name == "searchParams");
                _output.WriteLine("ReindexJobDocument:");
                var serializer = new FhirJsonSerializer();
                _output.WriteLine(serializer.SerializeToString(reindexJobResult.Resource));

                Assert.Contains(createdSearchParam.Resource.Url, param?.Value?.ToString());

                reindexJobResult = await WaitForReindexStatus(reindexJobUri, "Completed");
                _output.WriteLine($"Reindex job is completed, it should have reindexed the resources with name or id containing '{name}'.");

                bool floatParse = float.TryParse(
                    reindexJobResult.Resource.Parameter.FirstOrDefault(predicate => predicate.Name == "resourcesSuccessfullyReindexed").Value.ToString(),
                    out float resourcesReindexed);

                _output.WriteLine($"Reindex job is completed, {resourcesReindexed} resources Reindexed");

                Assert.True(floatParse);
                Assert.True(resourcesReindexed > 0.0);

                // After reindexing no need to use x-ms-use-partial-indices, all resources are searchable.
                await ExecuteAndValidateBundle(
                    $"Patient?{searchParameterName}={getParameter1(patientAWithTokenOverflow)}${getParameter2(patientAWithTokenOverflow)}",
                    createdPatientA);

                await ExecuteAndValidateBundle(
                    $"Patient?{searchParameterName}={getParameter1(patientBWithTokenOverflow)}${getParameter2(patientBWithTokenOverflow)}",
                    createdPatientB);

                await ExecuteAndValidateBundle(
                    $"Patient?{searchParameterName}={getParameter1(patientCWithNoTokenOverflow)}${getParameter2(patientCWithNoTokenOverflow)}",
                    createdPatientC);

                // Invlid composite search parameter returns nothing (we combine patients A and B).
                await ExecuteAndValidateBundle(
                    $"Patient?{searchParameterName}={getParameter1(patientAWithTokenOverflow)}${getParameter2(patientBWithTokenOverflow)}");
            }
            catch (Exception e)
            {
                _output.WriteLine($"Exception: {e.Message}");
                _output.WriteLine($"Stack Trace: {e.StackTrace}");
                throw;
            }
        }

        private async Task<FhirResponse<Parameters>> WaitForReindexStatus(System.Uri reindexJobUri, params string[] desiredStatus)
        {
            int checkReindexCount = 0;
            string currentStatus;
            FhirResponse<Parameters> reindexJobResult = null;
            do
            {
                reindexJobResult = await Client.CheckReindexAsync(reindexJobUri);
                currentStatus = reindexJobResult.Resource.Parameter.FirstOrDefault(p => p.Name == "status")?.Value.ToString();
                checkReindexCount++;
                await Task.Delay(1000);
            }
            while (!desiredStatus.Contains(currentStatus) && checkReindexCount < 20);

            if (checkReindexCount >= 20)
            {
                throw new Exception("ReindexJob did not complete within 20 seconds.");
            }

            return reindexJobResult;
        }

        public Task DisposeAsync() => Task.CompletedTask;
    }
}
