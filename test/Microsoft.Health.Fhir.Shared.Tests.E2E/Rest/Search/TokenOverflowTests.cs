// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Core.Extensions;
using Microsoft.Health.Fhir.Client;
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

        private delegate string GetOneParameter<T>(T patient);

        public async Task InitializeAsync()
        {
            await Client.DeleteAllResources(ResourceType.Patient, null); // TODO: do we delete resources in DB?

            // await Client.DeleteAllResources(ResourceType.Specimen, null);
            // await Client.DeleteAllResources(ResourceType.Immunization, null);
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

        private void LoadTestResources<T>(string name, out T resourceAWithTokenOverflowOut, out T resourceBWithTokenOverflowOut, out T resourceCWithNoTokenOverflowOut)
            where T : DomainResource
        {
            if (typeof(T) == typeof(Patient))
            {
                Patient patient = Samples.GetJsonSample<Patient>("TokenOverflowPatient");

                Patient patientAWithTokenOverflow = (Patient)patient.DeepCopy();
                patientAWithTokenOverflow.Id = $"Patient-Id-{name}-A";
                patientAWithTokenOverflow.Name[0].Family = $"{name}-A";
                patientAWithTokenOverflow.BirthDate = "2016-01-15";
                patientAWithTokenOverflow.Identifier[0].Value = GetTokenValue(name, "A");
                patientAWithTokenOverflow.Telecom[0].Value = "555-555-5555";
                patientAWithTokenOverflow.ManagingOrganization.Reference = "Organization-A";
                resourceAWithTokenOverflowOut = (T)(DomainResource)patientAWithTokenOverflow;

                Patient patientBWithTokenOverflow = (Patient)patient.DeepCopy();
                patientBWithTokenOverflow.Id = $"Patient-Id-{name}-B";
                patientBWithTokenOverflow.Name[0].Family = $"{name}-B";
                patientBWithTokenOverflow.BirthDate = "2016-01-16";
                patientBWithTokenOverflow.Identifier[0].Value = GetTokenValue(name, "B");
                patientBWithTokenOverflow.Telecom[0].Value = "555-555-5556";
                patientBWithTokenOverflow.ManagingOrganization.Reference = "Organization-B";
                resourceBWithTokenOverflowOut = (T)(DomainResource)patientBWithTokenOverflow;

                Patient patientCWithNoTokenOverflow = (Patient)patient.DeepCopy();
                patientCWithNoTokenOverflow.Id = $"Patient-Id-{name}-C";
                patientCWithNoTokenOverflow.Name[0].Family = $"{name}-C";
                patientCWithNoTokenOverflow.BirthDate = "2016-01-17";
                patientCWithNoTokenOverflow.Identifier[0].Value = GetTokenValue(name);
                patientCWithNoTokenOverflow.Telecom[0].Value = "555-555-5557";
                patientCWithNoTokenOverflow.ManagingOrganization.Reference = "Organization-C";
                resourceCWithNoTokenOverflowOut = (T)(DomainResource)patientCWithNoTokenOverflow;
            }
            else if (typeof(T) == typeof(ChargeItem))
            {
                ChargeItem chargeItem = Samples.GetJsonSample<ChargeItem>("TokenOverflowChargeItem");

                ChargeItem chargeItemAWithTokenOverflow = (ChargeItem)chargeItem.DeepCopy();
                chargeItemAWithTokenOverflow.Id = $"ChargeItem-Id-{name}-A";
#if R4 || R5
                chargeItemAWithTokenOverflow.Identifier[0].Value = GetTokenValue(name, "A");
#else
                chargeItemAWithTokenOverflow.Identifier.Value = GetTokenValue(name, "A");
#endif
                chargeItemAWithTokenOverflow.Quantity.Value = 1;
                resourceAWithTokenOverflowOut = (T)(DomainResource)chargeItemAWithTokenOverflow;

                ChargeItem chargeItemBWithTokenOverflow = (ChargeItem)chargeItem.DeepCopy();
                chargeItemBWithTokenOverflow.Id = $"ChargeItem-Id-{name}-B";
#if R4 || R5
                chargeItemBWithTokenOverflow.Identifier[0].Value = GetTokenValue(name, "B");
#else
                chargeItemBWithTokenOverflow.Identifier.Value = GetTokenValue(name, "B");
#endif
                chargeItemBWithTokenOverflow.Quantity.Value = 2;
                resourceBWithTokenOverflowOut = (T)(DomainResource)chargeItemBWithTokenOverflow;

                ChargeItem chargeItemCWithNoTokenOverflow = (ChargeItem)chargeItem.DeepCopy();
                chargeItemCWithNoTokenOverflow.Id = $"ChargeItem-Id-{name}-C";
#if R4 || R5
                chargeItemCWithNoTokenOverflow.Identifier[0].Value = GetTokenValue(name);
#else
                chargeItemCWithNoTokenOverflow.Identifier.Value = GetTokenValue(name);
#endif
                chargeItemCWithNoTokenOverflow.Quantity.Value = 3;
                resourceCWithNoTokenOverflowOut = (T)(DomainResource)chargeItemCWithNoTokenOverflow;
            }
            else if (typeof(T) == typeof(RiskAssessment))
            {
                RiskAssessment riskAssessment = Samples.GetJsonSample<RiskAssessment>("TokenOverflowRiskAssessment");

                RiskAssessment riskAssessmentAWithTokenOverflow = (RiskAssessment)riskAssessment.DeepCopy();
                riskAssessmentAWithTokenOverflow.Id = $"RiskAssessment-Id-{name}-A";
#if R4 || R5
                riskAssessmentAWithTokenOverflow.Identifier[0].Value = GetTokenValue(name, "A");
#else
                riskAssessmentAWithTokenOverflow.Identifier.Value = GetTokenValue(name, "A");
#endif
                riskAssessmentAWithTokenOverflow.Prediction[0].Probability = new FhirDecimal(111.111M);
                riskAssessmentAWithTokenOverflow.Prediction[1].Probability = new FhirDecimal(111.555M);
                resourceAWithTokenOverflowOut = (T)(DomainResource)riskAssessmentAWithTokenOverflow;

                RiskAssessment riskAssessmentBWithTokenOverflow = (RiskAssessment)riskAssessment.DeepCopy();
                riskAssessmentBWithTokenOverflow.Id = $"RiskAssessment-Id-{name}-B";
#if R4 || R5
                riskAssessmentBWithTokenOverflow.Identifier[0].Value = GetTokenValue(name, "B");
#else
                riskAssessmentBWithTokenOverflow.Identifier.Value = GetTokenValue(name, "B");
#endif
                riskAssessmentBWithTokenOverflow.Prediction[0].Probability = new FhirDecimal(222.111M);
                riskAssessmentBWithTokenOverflow.Prediction[1].Probability = new FhirDecimal(222.555M);
                resourceBWithTokenOverflowOut = (T)(DomainResource)riskAssessmentBWithTokenOverflow;

                RiskAssessment riskAssessmentCWithNoTokenOverflow = (RiskAssessment)riskAssessment.DeepCopy();
                riskAssessmentCWithNoTokenOverflow.Id = $"RiskAssessment-Id-{name}-C";
#if R4 || R5
                riskAssessmentCWithNoTokenOverflow.Identifier[0].Value = GetTokenValue(name);
#else
                riskAssessmentCWithNoTokenOverflow.Identifier.Value = GetTokenValue(name);
#endif
                riskAssessmentCWithNoTokenOverflow.Prediction[0].Probability = new FhirDecimal(333.111M);
                riskAssessmentCWithNoTokenOverflow.Prediction[1].Probability = new FhirDecimal(333.555M);
                resourceCWithNoTokenOverflowOut = (T)(DomainResource)riskAssessmentCWithNoTokenOverflow;
            }
            else
            {
                throw new Exception("Unsupported test resource type."); // Should never happen.
            }
        }

        // TODO: what do we skip Stu3 R5?
        [SkippableFact]
        public async Task GivenResourcesWithAndWithoutTokenOverflow_WhenSearchByToken_VerifyCorrectSerachResults()
        {
            try
            {
                string name = "Kirk-T-" + Guid.NewGuid().ToString().ComputeHash().Substring(0, 14).ToLower();

                LoadTestResources(name, out Patient patientAWithTokenOverflow, out Patient patientBWithTokenOverflow, out Patient patientCWithNoTokenOverflow);

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

        [SkippableFact]
        public async Task GivenResourcesWithAndWithoutTokenOverflow_WhenSearchByTokenString_VerifyCorrectSerachResults()
        {
            await TestCompositeTokenOverflow<Patient>(
                "Patient",
                "Kirk-CTS",
                "CompositeCustomTokenStringSearchParameter",
                patient => patient.Identifier[0].Value,
                patient => patient.Name[0].Family);
        }

        [SkippableFact]
        public async Task GivenResourcesWithAndWithoutTokenOverflow_WhenSearchByTokenDateTime_VerifyCorrectSerachResults()
        {
            await TestCompositeTokenOverflow<Patient>(
                "Patient",
                "Kirk-CTD",
                "CompositeCustomTokenDateTimeSearchParameter",
                patient => patient.Identifier[0].Value,
                patient => patient.BirthDate);
        }

        [SkippableFact]
        public async Task GivenResourcesWithAndWithoutTokenOverflow_WhenSearchByTokenOverflowToken_VerifyCorrectSerachResults()
        {
            await TestCompositeTokenOverflow<Patient>(
                "Patient",
                "Kirk-CToT",
                "CompositeCustomTokenOverflowTokenSearchParameter",
                patient => patient.Identifier[0].Value,
                patient => patient.Telecom[0].Value);
        }

        [SkippableFact]
        public async Task GivenResourcesWithAndWithoutTokenOverflow_WhenSearchByTokenTokenOverflowString_VerifyCorrectSerachResults()
        {
            await TestCompositeTokenOverflow<Patient>(
                "Patient",
                "Kirk-CTTo",
                "CompositeCustomTokenTokenOverflowSearchParameter",
                patient => patient.Telecom[0].Value,
                patient => patient.Identifier[0].Value);
        }

        [SkippableFact]
        public async Task GivenResourcesWithAndWithoutTokenOverflow_WhenSearchByReferenceToken_VerifyCorrectSerachResults()
        {
            await TestCompositeTokenOverflow<Patient>(
                "Patient",
                "Kirk-CRT",
                "CompositeCustomReferenceTokenSearchParameter",
                patient => patient.ManagingOrganization.Reference,
                patient => patient.Identifier[0].Value);
        }

        [SkippableFact]
        public async Task GivenResourcesWithAndWithoutTokenOverflow_WhenSearchByTokenQuantity_VerifyCorrectSerachResults()
        {
            await TestCompositeTokenOverflow<ChargeItem>(
                "ChargeItem",
                "Kirk-CTQ",
                "CompositeCustomTokenQuantitySearchParameter",
#if R4 || R5
                chargeItem => chargeItem.Identifier[0].Value,
#else
                chargeItem => chargeItem.Identifier.Value,
#endif
                chargeItem => chargeItem.Quantity.Value.ToString());
        }

        [SkippableFact]
        public async Task GivenResourcesWithAndWithoutTokenOverflow_WhenSearchByTokenNumberNumber_VerifyCorrectSerachResults()
        {
            await TestCompositeTokenOverflow<RiskAssessment>(
                "RiskAssessment",
                "Kirk-CTNN",
                "CompositeCustomTokenNumberNumberSearchParameter",
#if R4 || R5
                riskAssessment => riskAssessment.Identifier[0].Value,
#else
                riskAssessment => riskAssessment.Identifier.Value,
#endif
                riskAssessment =>
                {
                    decimal number1 = ((FhirDecimal)riskAssessment.Prediction[0].Probability).Value ?? 0;
                    decimal number2 = ((FhirDecimal)riskAssessment.Prediction[1].Probability).Value ?? 0;
                    return $"{number1}${number2}";
                });
        }

        private async Task TestCompositeTokenOverflow<T>(
            string resourceTypeName,
            string resourceNamePrefix,
            string searchParameterTestFileName,
            GetOneParameter<T> getParameter1,
            GetOneParameter<T> getParameter2)
            where T : DomainResource
        {
            try
            {
                // Randomize various names and id-s so tests can be rerun without clearing the database.
                string rnd = Guid.NewGuid().ToString().ComputeHash().Substring(0, 14).ToLower();
                string name = $"{resourceNamePrefix}-{rnd}";

                // Prepare search parameter settings.
                string searchParameterName = $"Search-{name}";
                SearchParameter searchParam = Samples.GetJsonSample<SearchParameter>(searchParameterTestFileName);
                searchParam.Id = $"Id-{searchParameterName}";
                searchParam.Name = searchParameterName;
                searchParam.Url = "http://hl7.org/fhir/SearchParameter/" + searchParameterName;
                searchParam.Code = searchParameterName;

                // Load test resources.
                LoadTestResources<T>(name, out T resourceAWithTokenOverflow, out T resourceBWithTokenOverflow, out T resourceCWithNoTokenOverflow);

                // First we create new resources and composite search parameter.

                // POST resource A, BEFORE composite search parameter is created.
                FhirResponse<T> createdResourceA = await Client.CreateAsync<T>(resourceAWithTokenOverflow);
                EnsureSuccessStatusCode(createdResourceA.StatusCode, "Creating resource A.");

                // POST custom composite search parameter.
                FhirResponse<SearchParameter> createdSearchParam = await Client.CreateAsync(searchParam);
                EnsureSuccessStatusCode(createdResourceA.StatusCode, "Creating custom composite search parameter.");

                // POST resource B.
                FhirResponse<T> createdResourceB = await Client.CreateAsync(resourceBWithTokenOverflow);
                EnsureSuccessStatusCode(createdResourceA.StatusCode, "Creating resource B.");

                // POST resource C.
                FhirResponse<T> createdResourceC = await Client.CreateAsync(resourceCWithNoTokenOverflow);
                EnsureSuccessStatusCode(createdResourceA.StatusCode, "Creating resource C.");

                // Before reindexing the database we test if we can access or not the created resources, with and without x-ms-use-partial-indices header.

                // Without x-ms-use-partial-indices header we cannot search for resources created after the search parameter was created.
                Bundle bundle = await Client.SearchAsync($"{resourceTypeName}?{searchParameterName}={getParameter1(resourceBWithTokenOverflow)}${getParameter2(resourceBWithTokenOverflow)}");
                OperationOutcome operationOutcome = GetAndValidateOperationOutcome(bundle);
                string[] expectedDiagnostics = { string.Format(Core.Resources.SearchParameterNotSupported, searchParameterName, resourceTypeName) };
                OperationOutcome.IssueSeverity[] expectedIssueSeverities = { OperationOutcome.IssueSeverity.Warning };
                OperationOutcome.IssueType[] expectedCodeTypes = { OperationOutcome.IssueType.NotSupported };
                ValidateOperationOutcome(expectedDiagnostics, expectedIssueSeverities, expectedCodeTypes, operationOutcome);

                // With x-ms-use-partial-indices header we can search only for resources created after the search parameter was created.

                await ExecuteAndValidateBundle(
                    $"{resourceTypeName}?{searchParameterName}={getParameter1(resourceAWithTokenOverflow)}${getParameter2(resourceAWithTokenOverflow)}",
                    false,
                    false,
                    new Tuple<string, string>("x-ms-use-partial-indices", "true")); // Nothing should be returned.

                await ExecuteAndValidateBundle(
                    $"{resourceTypeName}?{searchParameterName}={getParameter1(resourceBWithTokenOverflow)}${getParameter2(resourceBWithTokenOverflow)}",
                    false,
                    false,
                    new Tuple<string, string>("x-ms-use-partial-indices", "true"),
                    createdResourceB); // Expected resource B.

                await ExecuteAndValidateBundle(
                    $"{resourceTypeName}?{searchParameterName}={getParameter1(resourceCWithNoTokenOverflow)}${getParameter2(resourceCWithNoTokenOverflow)}",
                    false,
                    false,
                    new Tuple<string, string>("x-ms-use-partial-indices", "true"),
                    createdResourceC); // Expected resource C.

                // Start reindexing DB.

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

                // DB is now reindexed.

                // After reindexing no need to use x-ms-use-partial-indices, all resources are searchable.
                // Also, resources A and B have token overflow while C does not. Still all resources are correctly returned.
                await ExecuteAndValidateBundle(
                    $"{resourceTypeName}?{searchParameterName}={getParameter1(resourceAWithTokenOverflow)}${getParameter2(resourceAWithTokenOverflow)}",
                    createdResourceA); // Expected resource A.

                await ExecuteAndValidateBundle(
                    $"{resourceTypeName}?{searchParameterName}={getParameter1(resourceBWithTokenOverflow)}${getParameter2(resourceBWithTokenOverflow)}",
                    createdResourceB); // Expected resource B.

                await ExecuteAndValidateBundle(
                    $"{resourceTypeName}?{searchParameterName}={getParameter1(resourceCWithNoTokenOverflow)}${getParameter2(resourceCWithNoTokenOverflow)}",
                    createdResourceC); // Expected resource C.

                // Invlid composite search parameter returns nothing (we combine search parameters for resources A and B).
                await ExecuteAndValidateBundle(
                    $"{resourceTypeName}?{searchParameterName}={getParameter1(resourceAWithTokenOverflow)}${getParameter2(resourceBWithTokenOverflow)}");
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
