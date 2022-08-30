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
    [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer, Format.Json)]
    public class TokenOverflowTests : SearchTestsBase<HttpIntegrationTestFixture>, IAsyncLifetime
    {
        private readonly ITestOutputHelper _output;

        public TokenOverflowTests(HttpIntegrationTestFixture fixture, ITestOutputHelper output)
            : base(fixture)
        {
            _output = output;
        }

        // Delegate returns value to be used in the search parameter. If valid is set to false then syntactically correct value, but a value that is
        // not used by the resources in the test must be returned. The goal is then to test if database select logic correctly returns no data (rows).
        private delegate string GetOneParameter<T>(T resource, bool valid = true);

        public async Task InitializeAsync()
        {
            await Task.CompletedTask;
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

                // IMPORTANT. It is important that the following values are same for all the resources in a test, so selection of returned resources
                // from database is done based on proper functioning of the token overflow functionality only.
                patient.Name[0].Family = $"{name}-A"; // Used in token-string tests.
                patient.BirthDate = "2016-01-15"; // Used in token-datetime tests.
                patient.Telecom[0].Value = "555-555-5555"; // Used in tokenoverflow-token and token-tokenoverflow tests.
                patient.ManagingOrganization.Reference = "Test-Organization"; // Used in reference-token tests.

                Patient patientAWithTokenOverflow = (Patient)patient.DeepCopy();
                patient.Id = $"Patient-Id-{name}-A";
                patientAWithTokenOverflow.Identifier[0].Value = GetTokenValue(name, "A"); // Overflow A.
                resourceAWithTokenOverflowOut = (T)(DomainResource)patientAWithTokenOverflow;

                Patient patientBWithTokenOverflow = (Patient)patient.DeepCopy();
                patientBWithTokenOverflow.Id = $"Patient-Id-{name}-B";
                patientBWithTokenOverflow.Identifier[0].Value = GetTokenValue(name, "B"); // Overflow B.
                resourceBWithTokenOverflowOut = (T)(DomainResource)patientBWithTokenOverflow;

                Patient patientCWithNoTokenOverflow = (Patient)patient.DeepCopy();
                patientCWithNoTokenOverflow.Id = $"Patient-Id-{name}-C";
                patientCWithNoTokenOverflow.Identifier[0].Value = GetTokenValue(name); // NO overflow.
                resourceCWithNoTokenOverflowOut = (T)(DomainResource)patientCWithNoTokenOverflow;
            }
            else if (typeof(T) == typeof(ChargeItem))
            {
                ChargeItem chargeItem = Samples.GetJsonSample<ChargeItem>("TokenOverflowChargeItem");

                // IMPORTANT. It is important that the following value is same for all the resources in a test for the same reason as above.
                chargeItem.Quantity.Value = 1; // Used in token quantity tests.

                ChargeItem chargeItemAWithTokenOverflow = (ChargeItem)chargeItem.DeepCopy();
                chargeItemAWithTokenOverflow.Id = $"ChargeItem-Id-{name}-A";
#if R4 || R5
                chargeItemAWithTokenOverflow.Identifier[0].Value = GetTokenValue(name, "A"); // Overflow A.
#else
                chargeItemAWithTokenOverflow.Identifier.Value = GetTokenValue(name, "A"); // Overflow A.
#endif
                resourceAWithTokenOverflowOut = (T)(DomainResource)chargeItemAWithTokenOverflow;

                ChargeItem chargeItemBWithTokenOverflow = (ChargeItem)chargeItem.DeepCopy();
                chargeItemBWithTokenOverflow.Id = $"ChargeItem-Id-{name}-B";
#if R4 || R5
                chargeItemBWithTokenOverflow.Identifier[0].Value = GetTokenValue(name, "B"); // Overflow B.
#else
                chargeItemBWithTokenOverflow.Identifier.Value = GetTokenValue(name, "B"); // Overflow B.
#endif
                resourceBWithTokenOverflowOut = (T)(DomainResource)chargeItemBWithTokenOverflow;

                ChargeItem chargeItemCWithNoTokenOverflow = (ChargeItem)chargeItem.DeepCopy();
                chargeItemCWithNoTokenOverflow.Id = $"ChargeItem-Id-{name}-C";
#if R4 || R5
                chargeItemCWithNoTokenOverflow.Identifier[0].Value = GetTokenValue(name); // NO overflow.
#else
                chargeItemCWithNoTokenOverflow.Identifier.Value = GetTokenValue(name); // NO overflow.
#endif
                resourceCWithNoTokenOverflowOut = (T)(DomainResource)chargeItemCWithNoTokenOverflow;
            }
            else if (typeof(T) == typeof(RiskAssessment))
            {
                RiskAssessment riskAssessment = Samples.GetJsonSample<RiskAssessment>("TokenOverflowRiskAssessment");

                // IMPORTANT. It is important that the following values are same for all the resources in a test for the same reason as above.
                riskAssessment.Prediction[0].Probability = new FhirDecimal(111.111M); // Used in token-number-number tests.
                riskAssessment.Prediction[1].Probability = new FhirDecimal(111.555M); // Used in token-number-number tests.

                RiskAssessment riskAssessmentAWithTokenOverflow = (RiskAssessment)riskAssessment.DeepCopy();
                riskAssessmentAWithTokenOverflow.Id = $"RiskAssessment-Id-{name}-A";
#if R4 || R5
                riskAssessmentAWithTokenOverflow.Identifier[0].Value = GetTokenValue(name, "A"); // Overflow A.
#else
                riskAssessmentAWithTokenOverflow.Identifier.Value = GetTokenValue(name, "A"); // Overflow A.
#endif
                resourceAWithTokenOverflowOut = (T)(DomainResource)riskAssessmentAWithTokenOverflow;

                RiskAssessment riskAssessmentBWithTokenOverflow = (RiskAssessment)riskAssessment.DeepCopy();
                riskAssessmentBWithTokenOverflow.Id = $"RiskAssessment-Id-{name}-B";
#if R4 || R5
                riskAssessmentBWithTokenOverflow.Identifier[0].Value = GetTokenValue(name, "B"); // Overflow B.
#else
                riskAssessmentBWithTokenOverflow.Identifier.Value = GetTokenValue(name, "B"); // Overflow B.
#endif
                resourceBWithTokenOverflowOut = (T)(DomainResource)riskAssessmentBWithTokenOverflow;

                RiskAssessment riskAssessmentCWithNoTokenOverflow = (RiskAssessment)riskAssessment.DeepCopy();
                riskAssessmentCWithNoTokenOverflow.Id = $"RiskAssessment-Id-{name}-C";
#if R4 || R5
                riskAssessmentCWithNoTokenOverflow.Identifier[0].Value = GetTokenValue(name); // NO overflow.
#else
                riskAssessmentCWithNoTokenOverflow.Identifier.Value = GetTokenValue(name); // NO overflow.
#endif
                resourceCWithNoTokenOverflowOut = (T)(DomainResource)riskAssessmentCWithNoTokenOverflow;
            }
            else
            {
                throw new Exception("Unsupported test resource type."); // Should never happen.
            }
        }

        [Fact]
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

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task GivenResourcesWithAndWithoutTokenOverflow_WhenSearchByTokenString_VerifyCorrectSerachResults(bool singleReindex)
        {
            await TestCompositeTokenOverflow<Patient>(
                singleReindex,
                "Patient",
                "Kirk-CTS",
                "CompositeCustomTokenStringSearchParameter",
                (patient, valid) => patient.Identifier[0].Value,
                (patient, valid) => valid ? patient.Name[0].Family : "INVALID"); // IMPORTANT, must be a value that is not used by the resources.
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task GivenResourcesWithAndWithoutTokenOverflow_WhenSearchByTokenDateTime_VerifyCorrectSerachResults(bool singleReindex)
        {
            await TestCompositeTokenOverflow<Patient>(
                singleReindex,
                "Patient",
                "Kirk-CTD",
                "CompositeCustomTokenDateTimeSearchParameter",
                (patient, valid) => patient.Identifier[0].Value,
                (patient, valid) => valid ? patient.BirthDate : "2000-01-01"); // IMPORTANT, must be a value that is not used by the resources.
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task GivenResourcesWithAndWithoutTokenOverflow_WhenSearchByTokenOverflowToken_VerifyCorrectSerachResults(bool singleReindex)
        {
            await TestCompositeTokenOverflow<Patient>(
                singleReindex,
                "Patient",
                "Kirk-CToT",
                "CompositeCustomTokenOverflowTokenSearchParameter",
                (patient, valid) => patient.Identifier[0].Value,
                (patient, valid) => valid ? patient.Telecom[0].Value : "111-111-1111"); // IMPORTANT, must be a value that is not used by the resources.
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task GivenResourcesWithAndWithoutTokenOverflow_WhenSearchByTokenTokenOverflowString_VerifyCorrectSerachResults(bool singleReindex)
        {
            await TestCompositeTokenOverflow<Patient>(
                singleReindex,
                "Patient",
                "Kirk-CTTo",
                "CompositeCustomTokenTokenOverflowSearchParameter",
                (patient, valid) => valid ? patient.Telecom[0].Value : "111-111-1111", // IMPORTANT, must be a value that is not used by the resources.
                (patient, valid) => patient.Identifier[0].Value);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task GivenResourcesWithAndWithoutTokenOverflow_WhenSearchByReferenceToken_VerifyCorrectSerachResults(bool singleReindex)
        {
            await TestCompositeTokenOverflow<Patient>(
                singleReindex,
                "Patient",
                "Kirk-CRT",
                "CompositeCustomReferenceTokenSearchParameter",
                (patient, valid) => valid ? patient.ManagingOrganization.Reference : "INVALID-REFERENCE", // IMPORTANT, must be a value that is not used by the resources.
                (patient, valid) => patient.Identifier[0].Value);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task GivenResourcesWithAndWithoutTokenOverflow_WhenSearchByTokenQuantity_VerifyCorrectSerachResults(bool singleReindex)
        {
            await TestCompositeTokenOverflow<ChargeItem>(
                singleReindex,
                "ChargeItem",
                "Kirk-CTQ",
                "CompositeCustomTokenQuantitySearchParameter",
#if R4 || R5
                (chargeItem, valid) => chargeItem.Identifier[0].Value,
#else
                (chargeItem, valid) => chargeItem.Identifier.Value,
#endif
                (chargeItem, valid) => valid ? chargeItem.Quantity.Value.ToString() : "555"); // IMPORTANT, must be a value that is not used by the resources.
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task GivenResourcesWithAndWithoutTokenOverflow_WhenSearchByTokenNumberNumber_VerifyCorrectSerachResults(bool singleReindex)
        {
            await TestCompositeTokenOverflow<RiskAssessment>(
                singleReindex,
                "RiskAssessment",
                "Kirk-CTNN",
                "CompositeCustomTokenNumberNumberSearchParameter",
#if R4 || R5
                (riskAssessment, valid) => riskAssessment.Identifier[0].Value,
#else
                (riskAssessment, valid) => riskAssessment.Identifier.Value,
#endif
                (riskAssessment, valid) =>
                {
                    decimal number1 = 000.999M; // IMPORTANT, must be a value that is not used by the resources.
                    decimal number2 = 000.999M; // IMPORTANT, must be a value that is not used by the resources.
                    if (valid)
                    {
                        number1 = ((FhirDecimal)riskAssessment.Prediction[0].Probability).Value ?? 0;
                        number2 = ((FhirDecimal)riskAssessment.Prediction[1].Probability).Value ?? 0;
                    }

                    return $"{number1}${number2}";
                });
        }

        private async Task TestCompositeTokenOverflow<T>(
            bool singleReindex,
            string resourceTypeName,
            string resourceNamePrefix,
            string searchParameterTestFileName,
            GetOneParameter<T> getParameter1,
            GetOneParameter<T> getParameter2)
            where T : DomainResource
        {
            try
            {
                // Stu3 composite search parameter files are slightly different.
#if Stu3
                searchParameterTestFileName += "Stu3";
#endif

                // Randomize various names and id-s so tests can be rerun without clearing the database.
                string rnd = Guid.NewGuid().ToString().ComputeHash().Substring(0, 14).ToLower();
                string name = $"{resourceNamePrefix}-{(singleReindex ? 'S' : 'F')}-{rnd}";

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

                // Start reindexing resources.

                if (singleReindex == false)
                {
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
                }
                else
                {
                    // Single resources are reindexed immediately.

                    FhirResponse<Parameters> responseA;
                    (responseA, _) = await Client.PostReindexJobAsync(new Parameters(), $"{resourceTypeName}/{createdResourceA.Resource.Id}/");
                    EnsureSuccessStatusCode(createdResourceA.StatusCode, $"Reindexing resource {resourceTypeName}/{createdResourceA.Resource.Id}.");
                    Assert.True(responseA.Resource.Parameter.Count > 0);

                    FhirResponse<Parameters> responseB;
                    (responseB, _) = await Client.PostReindexJobAsync(new Parameters(), $"{resourceTypeName}/{createdResourceB.Resource.Id}/");
                    EnsureSuccessStatusCode(createdResourceA.StatusCode, $"Reindexing resource {resourceTypeName}/{createdResourceB.Resource.Id}.");
                    Assert.True(responseB.Resource.Parameter.Count > 0);

                    FhirResponse<Parameters> responseC;
                    (responseC, _) = await Client.PostReindexJobAsync(new Parameters(), $"{resourceTypeName}/{createdResourceC.Resource.Id}/");
                    EnsureSuccessStatusCode(createdResourceA.StatusCode, $"Reindexing resource {resourceTypeName}/{createdResourceC.Resource.Id}.");
                    Assert.True(responseC.Resource.Parameter.Count > 0);
                }

                // Resources are now reindexed.

                // After reindexing, if full database is reindexed no need to use x-ms-use-partial-indices, all resources are searchable.
                // Otherwise, must use x-ms-use-partial-indices header.
                // Also, resources A and B have token overflow while C does not. Still all resources are correctly returned.
                await ExecuteAndValidateBundle(
                    $"{resourceTypeName}?{searchParameterName}={getParameter1(resourceAWithTokenOverflow)}${getParameter2(resourceAWithTokenOverflow)}",
                    false,
                    false,
                    singleReindex ? new Tuple<string, string>("x-ms-use-partial-indices", "true") : null,
                    createdResourceA); // Expected resource A.

                await ExecuteAndValidateBundle(
                    $"{resourceTypeName}?{searchParameterName}={getParameter1(resourceBWithTokenOverflow)}${getParameter2(resourceBWithTokenOverflow)}",
                    false,
                    false,
                    singleReindex ? new Tuple<string, string>("x-ms-use-partial-indices", "true") : null,
                    createdResourceB); // Expected resource B.

                await ExecuteAndValidateBundle(
                    $"{resourceTypeName}?{searchParameterName}={getParameter1(resourceCWithNoTokenOverflow)}${getParameter2(resourceCWithNoTokenOverflow)}",
                    false,
                    false,
                    singleReindex ? new Tuple<string, string>("x-ms-use-partial-indices", "true") : null,
                    createdResourceC); // Expected resource C.

                // Invalid composite search parameter returns nothing, we send correct token but incorrect second parameter that is not used by any of the resources.
                await ExecuteAndValidateBundle(
                    $"{resourceTypeName}?{searchParameterName}={getParameter1(resourceAWithTokenOverflow, false)}${getParameter2(resourceBWithTokenOverflow, false)}",
                    false,
                    false,
                    singleReindex ? new Tuple<string, string>("x-ms-use-partial-indices", "true") : null);
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
