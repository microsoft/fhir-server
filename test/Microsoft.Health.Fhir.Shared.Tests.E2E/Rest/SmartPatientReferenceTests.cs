// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Fhir.Web;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Authorization)]
    [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer, Format.Json)]
    public class SmartPatientReferenceTests : IClassFixture<HttpIntegrationTestFixture>
    {
        private const string StorageMessage = "cannot be indexed or searched without loss";

        private readonly HttpIntegrationTestFixture _fixture;

        public SmartPatientReferenceTests(HttpIntegrationTestFixture fixture)
        {
            _fixture = fixture;
        }

        [Theory]
        [InlineData("", true)]
        [InlineData(null, true)]
        [InlineData("https://external.fhir.invalid/", false)]
        [InlineData("other-fhir/", false)]
        public async Task GivenPatientIdCollision_WhenSearchingAndReading_ThenOnlyLocalReferencesAuthorizeAccess(string referenceBase, bool isLocal)
        {
            Assert.SkipWhen(!_fixture.IsUsingInProcTestServer, "Requires the development identity provider to issue a patient-scoped token.");
            var adminClient = _fixture.TestFhirClient.CreateClientForClientApplication(TestApplications.AdminUser);
            string reference = referenceBase switch
            {
                null => new Uri(_fixture.TestFhirServer.BaseAddress, "Patient/smart-patient-A").AbsoluteUri,
                "other-fhir/" => new Uri(_fixture.TestFhirServer.BaseAddress, "other-fhir/Patient/smart-patient-A").AbsoluteUri,
                _ => $"{referenceBase}Patient/smart-patient-A",
            };
            Observation observation = await adminClient.CreateAsync(new Observation
            {
                Status = ObservationStatus.Final,
                Code = new CodeableConcept("http://loinc.org", "1234-5"),
                Subject = new ResourceReference(reference),
                Value = new FhirString("synthetic-compartment-collision"),
            });

            try
            {
                using var client = new HttpClient(_fixture.TestFhirServer.CreateMessageHandler())
                {
                    BaseAddress = _fixture.TestFhirServer.BaseAddress,
                };
                TestApplication application = TestApplications.SmartPatientA;
                using var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    { "grant_type", application.GrantType },
                    { "client_id", application.ClientId },
                    { "client_secret", application.ClientSecret },
                    { "scope", "patient/Observation.read" },
                });
                using HttpResponseMessage tokenResponse = await client.PostAsync(_fixture.TestFhirServer.TokenUri, tokenRequest);
                tokenResponse.EnsureSuccessStatusCode();
                using JsonDocument token = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.RootElement.GetProperty("access_token").GetString());

                foreach (string query in new[] { "?_count=1000", $"?_id={observation.Id}", "?subject=Patient/smart-patient-A" })
                {
                    bool found = false;
                    string nextPage = $"Observation{query}";
                    do
                    {
                        using HttpResponseMessage response = await client.GetAsync(nextPage);
                        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                        var bundle = new FhirJsonParser().Parse<Bundle>(await response.Content.ReadAsStringAsync());
                        found |= bundle.Entry.Any(entry => entry.Resource is Observation result && result.Id == observation.Id);
                        nextPage = bundle.NextLink?.OriginalString;
                    }
                    while (nextPage != null);

                    Assert.Equal(isLocal, found);
                }

                using HttpResponseMessage read = await client.GetAsync($"Observation/{observation.Id}");
                Assert.Equal(isLocal ? HttpStatusCode.OK : HttpStatusCode.NotFound, read.StatusCode);
                if (isLocal)
                {
                    using JsonDocument resource = JsonDocument.Parse(await read.Content.ReadAsStringAsync());
                    Assert.Equal("synthetic-compartment-collision", resource.RootElement.GetProperty("valueString").GetString());
                }
            }
            finally
            {
                await adminClient.HardDeleteAsync(observation);
            }
        }

        [Fact]
        public async Task GivenDistinctMaximumLengthReferenceBases_WhenScopedBySubject_ThenOnlyTheGrantedBaseAuthorizesAccess()
        {
            Assert.SkipWhen(!_fixture.IsUsingInProcTestServer, "Requires the development identity provider to issue a scoped token.");
            var resources = new TrackedResources(_fixture.TestFhirClient.CreateClientForClientApplication(TestApplications.AdminUser));
            string runId = NewRunId();
            string patientPath = $"Patient/p{runId}";
            string grantedSubject = CreateBase(runId, 128, "x") + patientPath;
            string otherSubject = CreateBase(runId, 128, "y") + patientPath;

            await resources.RunAsync(async () =>
            {
                Observation granted = await resources.CreateAsync(CreateObservation(grantedSubject));
                Observation other = await resources.CreateAsync(CreateObservation(otherSubject));
                DiagnosticReport report = await resources.CreateAsync(new DiagnosticReport
                {
                    Status = DiagnosticReport.DiagnosticReportStatus.Final,
                    Code = new CodeableConcept("http://loinc.org", "1234-5"),
                    Subject = new ResourceReference(grantedSubject),
                    Result = new List<ResourceReference>
                    {
                        new ResourceReference($"Observation/{granted.Id}"),
                        new ResourceReference($"Observation/{other.Id}"),
                    },
                });
                using HttpClient client = await CreateSmartClientAsync(
                    TestApplications.SmartUserClient,
                    $"system/Observation.rs?subject={grantedSubject} system/DiagnosticReport.rs?subject={grantedSubject}");

                Assert.Equal(Keys(granted), await SearchKeysAsync(client, $"Observation?_id={granted.Id},{other.Id}"));
                Assert.Equal(Keys(report, granted), await SearchKeysAsync(client, $"DiagnosticReport?_id={report.Id}&_include=DiagnosticReport:result"));
                using HttpResponseMessage grantedRead = await client.GetAsync($"Observation/{granted.Id}");
                Assert.Equal(HttpStatusCode.OK, grantedRead.StatusCode);
                using HttpResponseMessage otherRead = await client.GetAsync($"Observation/{other.Id}");
                Assert.Equal(HttpStatusCode.NotFound, otherRead.StatusCode);
            });
        }

        [Fact]
        public async Task GivenReferenceBaseUrisAroundTheStorageLimit_WhenWritingAndSearching_ThenOnlyWholeBasesAreAccepted()
        {
            var resources = new TrackedResources(_fixture.TestFhirClient.CreateClientForClientApplication(TestApplications.AdminUser));
            string runId = NewRunId();
            string patientPath = $"Patient/p{runId}";
            string subject127 = CreateBase(runId, 127, "x") + patientPath;
            string subject128 = CreateBase(runId, 128, "x") + patientPath;
            string subject129 = CreateBase(runId, 129, "x") + patientPath;
            string subject134 = CreateBase(runId, 128, "x") + "other/" + patientPath;
            string nonCodePageSubject = $"https://ext-{runId}.invalid/\u4F8B/{patientPath}";

            await resources.RunAsync(async () =>
            {
                Observation observation127 = await resources.CreateAsync(CreateObservation(subject127));
                Observation observation128 = await resources.CreateAsync(CreateObservation(subject128));
                Assert.Equal(Keys(observation127), await SearchKeysAsync(resources.AdminClient.HttpClient, $"Observation?subject={Uri.EscapeDataString(subject127)}"));
                Assert.Equal(Keys(observation128), await SearchKeysAsync(resources.AdminClient.HttpClient, $"Observation?subject={Uri.EscapeDataString(subject128)}"));

                foreach (string subject in new[] { subject129, subject134, nonCodePageSubject })
                {
                    AssertRejected(await resources.SendAsync(HttpMethod.Post, "Observation", CreateObservation(subject)), HttpStatusCode.BadRequest, StorageMessage, runId);
                }

                Observation replacement = CreateObservation(subject129);
                replacement.Id = observation128.Id;
                AssertRejected(await resources.SendAsync(HttpMethod.Put, $"Observation/{observation128.Id}", replacement), HttpStatusCode.BadRequest, StorageMessage, runId);
                using FhirResponse<Observation> current = await resources.AdminClient.ReadAsync<Observation>(ResourceType.Observation, observation128.Id);
                Assert.Equal("1", current.Resource.Meta.VersionId);
                Assert.Equal(subject128, current.Resource.Subject.Reference);

                AssertRejected(await resources.SendAsync(HttpMethod.Get, $"Observation?subject={Uri.EscapeDataString(subject134)}"), HttpStatusCode.BadRequest, StorageMessage, runId);
            });
        }

        [Theory]
        [InlineData(false, 64)]
        [InlineData(true, 128)]
        public async Task GivenResourceIdsAroundThePolicyLimit_WhenWritingSearchingAndValidating_ThenLongerIdsAreRejectedWithoutEcho(bool useLongResourceIds, int maximumLength)
        {
            Assert.SkipWhen(!_fixture.IsUsingInProcTestServer, "Requires startup-selected resource ID policy variants.");
            await using var server = new InProcTestFhirServer(
                DataStore.SqlServer,
                useLongResourceIds ? typeof(StartupWithLongResourceIds) : typeof(Startup));
            await server.ConfigureSecurityOptions();
            var resources = new TrackedResources(server.GetTestFhirClient(Hl7.Fhir.Rest.ResourceFormat.Json, reusable: false).CreateClientForClientApplication(TestApplications.AdminUser));
            string runId = NewRunId();
            string maximumId = $"p{runId}".PadRight(maximumLength, 'a');
            string overlongId = maximumId + "a";
            string idLengthMessage = $"length limit of {maximumLength}";

            await resources.RunAsync(async () =>
            {
                foreach (string id in new[] { maximumId[..^1], maximumId })
                {
                    var patient = new Patient { Id = id };
                    await resources.UpsertAsync(patient);
                    Observation observation = await resources.CreateAsync(CreateObservation($"Patient/{id}"));
                    Assert.Equal($"Patient/{id}", observation.Subject.Reference);
                    Assert.Equal(Keys(patient), await SearchKeysAsync(resources.AdminClient.HttpClient, $"Patient?_id={id}"));
                    Assert.Equal(Keys(observation), await SearchKeysAsync(resources.AdminClient.HttpClient, $"Observation?subject=Patient/{id}"));
                }

                AssertRejected(await resources.SendAsync(HttpMethod.Post, "Observation", CreateObservation($"Patient/{overlongId}")), HttpStatusCode.BadRequest, idLengthMessage, runId);
                AssertRejected(await resources.SendAsync(HttpMethod.Put, $"Patient/{overlongId}", new Patient { Id = overlongId }), HttpStatusCode.BadRequest, null, runId);
                AssertRejected(await resources.SendAsync(HttpMethod.Get, $"Observation?subject=Patient/{overlongId}"), HttpStatusCode.BadRequest, idLengthMessage, runId);
                AssertRejected(await resources.SendAsync(HttpMethod.Get, $"Patient?_id={overlongId}"), HttpStatusCode.BadRequest, idLengthMessage, runId);

                (HttpStatusCode status, string body) = await resources.SendAsync(HttpMethod.Post, "Observation/$validate", CreateObservation($"Patient/{overlongId}"));
                Assert.True(status == HttpStatusCode.OK, $"{status}: {body}");
                Assert.Contains(idLengthMessage, Diagnostics(body), StringComparison.Ordinal);
            });
        }

        [Fact]
        public async Task GivenValuesOutsideReferenceIdentityRules_WhenWritingAndSearching_ThenExistingBehaviorIsPreserved()
        {
            var resources = new TrackedResources(_fixture.TestFhirClient.CreateClientForClientApplication(TestApplications.AdminUser));
            string runId = NewRunId();
            string prefix = $"https://ext-{runId}.invalid/";
            string longSystem = prefix + new string('s', 200 - prefix.Length);

            await resources.RunAsync(async () =>
            {
                Observation coded = CreateObservation($"Patient/p{runId}");
                coded.Code = new CodeableConcept(longSystem, "c");
                coded = await resources.CreateAsync(coded);
                Assert.Equal(Keys(coded), await SearchKeysAsync(resources.AdminClient.HttpClient, $"Observation?code={Uri.EscapeDataString(longSystem + "|c")}"));

                Observation identified = CreateObservation(null);
                identified.Subject = new ResourceReference { Identifier = new Identifier(prefix + "patients", $"p{runId}") };
                await resources.CreateAsync(identified);

                Observation contained = CreateObservation("#p1");
                contained.Contained = new List<Resource> { new Patient { Id = "p1" } };
                await resources.CreateAsync(contained);
#if !Stu3

                await resources.CreateAsync(new QuestionnaireResponse
                {
                    Status = QuestionnaireResponse.QuestionnaireResponseStatus.Completed,
                    Questionnaire = $"{prefix}Questionnaire/q{runId}|1.0#frag",
                });
#endif
            });
        }
#if R4

        [Fact]
        public async Task GivenCompositeReferenceBasesAroundTheStorageLimit_WhenWritingAndSearching_ThenOnlyWholeBasesAreAccepted()
        {
            var resources = new TrackedResources(_fixture.TestFhirClient.CreateClientForClientApplication(TestApplications.AdminUser));
            string runId = NewRunId();
            string targetPath = $"DocumentReference/d{runId}";
            string target127 = CreateBase(runId, 127, "x") + targetPath;
            string target128 = CreateBase(runId, 128, "x") + targetPath;
            string target134 = CreateBase(runId, 128, "x") + "other/" + targetPath;

            await resources.RunAsync(async () =>
            {
                DocumentReference document = await resources.CreateAsync(CreateDocumentReference(target128));
                Assert.Equal(Keys(document), await SearchKeysAsync(resources.AdminClient.HttpClient, $"DocumentReference?relationship={Uri.EscapeDataString(target128 + "$replaces")}"));
                Assert.Empty(await SearchKeysAsync(resources.AdminClient.HttpClient, $"DocumentReference?relationship={Uri.EscapeDataString(target127 + "$replaces")}"));

                AssertRejected(await resources.SendAsync(HttpMethod.Post, "DocumentReference", CreateDocumentReference(target134)), HttpStatusCode.BadRequest, StorageMessage, runId);
                AssertRejected(await resources.SendAsync(HttpMethod.Get, $"DocumentReference?relationship={Uri.EscapeDataString(target134 + "$replaces")}"), HttpStatusCode.BadRequest, StorageMessage, runId);
            });
        }
#endif

        [Fact]
        public async Task GivenUnrepresentableSmartScopeReference_WhenSearchingAndReading_ThenAccessFailsClosed()
        {
            Assert.SkipWhen(!_fixture.IsUsingInProcTestServer, "Requires the development identity provider to issue a scoped token.");
            var resources = new TrackedResources(_fixture.TestFhirClient.CreateClientForClientApplication(TestApplications.AdminUser));
            string runId = NewRunId();
            string patientPath = $"Patient/p{runId}";
            string baseX = CreateBase(runId, 128, "x");
            string subjectX = baseX + patientPath;
            string subjectY = CreateBase(runId, 128, "y") + patientPath;
            string subject134 = baseX + "other/" + patientPath;

            await resources.RunAsync(async () =>
            {
                Observation observationX = await resources.CreateAsync(CreateObservation(subjectX));
                Observation observationY = await resources.CreateAsync(CreateObservation(subjectY));
                string search = $"Observation?_id={observationX.Id},{observationY.Id}";

                using (HttpClient client = await CreateSmartClientAsync(TestApplications.SmartUserClient, $"system/Observation.rs?subject={subject134}"))
                {
                    AssertRejected(await SendRawAsync(client, HttpMethod.Get, search), HttpStatusCode.Forbidden, "'subject'", runId);
                }

                using (HttpClient client = await CreateSmartClientAsync(TestApplications.SmartUserClient, $"system/*.rs?subject={subject134}"))
                {
                    AssertRejected(await SendRawAsync(client, HttpMethod.Get, search), HttpStatusCode.Forbidden, null, runId);
                }

                using (HttpClient client = await CreateSmartClientAsync(TestApplications.SmartUserClient, "system/*.rs"))
                {
                    Assert.Equal(Keys(observationX, observationY), await SearchKeysAsync(client, search));
                    AssertRejected(await SendRawAsync(client, HttpMethod.Get, $"Observation?subject={Uri.EscapeDataString(subject134)}"), HttpStatusCode.BadRequest, StorageMessage, runId);
                }

                using (HttpClient client = await CreateSmartClientAsync(
                    TestApplications.SmartUserClient,
                    $"system/Observation.rs?subject={subject134} system/Observation.rs?subject={subjectY}"))
                {
                    Assert.Equal(Keys(observationY), await SearchKeysAsync(client, search));
                    using HttpResponseMessage readX = await client.GetAsync($"Observation/{observationX.Id}");
                    Assert.Equal(HttpStatusCode.NotFound, readX.StatusCode);
                    using HttpResponseMessage readY = await client.GetAsync($"Observation/{observationY.Id}");
                    Assert.Equal(HttpStatusCode.OK, readY.StatusCode);
                }
            });
        }

        private static string NewRunId() => Guid.NewGuid().ToString("N")[..8];

        private static string CreateBase(string runId, int length, string marker)
        {
            string prefix = $"https://ext-{runId}.invalid/";
            string value = prefix + new string('a', length - prefix.Length - marker.Length - 1) + marker + "/";
            Assert.Equal(length, value.Length);
            return value;
        }

        private static Observation CreateObservation(string subject) => new Observation
        {
            Status = ObservationStatus.Final,
            Code = new CodeableConcept("http://loinc.org", "1234-5"),
            Subject = subject == null ? null : new ResourceReference(subject),
        };
#if R4

        private static DocumentReference CreateDocumentReference(string target) => new DocumentReference
        {
            Status = DocumentReferenceStatus.Current,
            Content = new List<DocumentReference.ContentComponent>
            {
                new DocumentReference.ContentComponent { Attachment = new Attachment { ContentType = "text/plain", Data = Encoding.UTF8.GetBytes("synthetic") } },
            },
            RelatesTo = new List<DocumentReference.RelatesToComponent>
            {
                new DocumentReference.RelatesToComponent { Code = DocumentRelationshipType.Replaces, Target = new ResourceReference(target) },
            },
        };
#endif

        private static List<string> Keys(params Resource[] resources) =>
            resources.Select(resource => $"{resource.TypeName}/{resource.Id}").OrderBy(key => key, StringComparer.Ordinal).ToList();

        private static async Task<List<string>> SearchKeysAsync(HttpClient client, string uri)
        {
            var keys = new List<string>();
            var parser = new FhirJsonParser();
            string nextPage = uri;
            do
            {
                using HttpResponseMessage response = await client.GetAsync(nextPage);
                string body = await response.Content.ReadAsStringAsync();
                Assert.True(response.StatusCode == HttpStatusCode.OK, $"{response.StatusCode}: {body}");
                var bundle = parser.Parse<Bundle>(body);
                keys.AddRange(bundle.Entry.Where(entry => entry.Resource is not OperationOutcome).Select(entry => $"{entry.Resource.TypeName}/{entry.Resource.Id}"));
                nextPage = bundle.NextLink?.OriginalString;
            }
            while (nextPage != null);

            return keys.OrderBy(key => key, StringComparer.Ordinal).ToList();
        }

        private static async Task<(HttpStatusCode Status, string Body)> SendRawAsync(HttpClient client, HttpMethod method, string uri, Resource resource = null)
        {
            using var request = new HttpRequestMessage(method, uri);
            if (resource != null)
            {
                request.Content = new StringContent(resource.ToJson(), Encoding.UTF8, "application/fhir+json");
            }

            using HttpResponseMessage response = await client.SendAsync(request);
            return (response.StatusCode, await response.Content.ReadAsStringAsync());
        }

        private static void AssertRejected((HttpStatusCode Status, string Body) response, HttpStatusCode expectedStatus, string expectedDiagnostics, string forbiddenValue)
        {
            Assert.True(response.Status == expectedStatus, $"{response.Status}: {response.Body}");
            if (expectedDiagnostics != null)
            {
                Assert.Contains(expectedDiagnostics, Diagnostics(response.Body), StringComparison.Ordinal);
            }

            Assert.DoesNotContain(forbiddenValue, response.Body, StringComparison.Ordinal);
        }

        private static string Diagnostics(string body) =>
            string.Join(Environment.NewLine, new FhirJsonParser().Parse<OperationOutcome>(body).Issue.Select(issue => issue.Diagnostics ?? issue.Details?.Text));

        private async Task<HttpClient> CreateSmartClientAsync(TestApplication application, string scope)
        {
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "grant_type", application.GrantType },
                { "client_id", application.ClientId },
                { "client_secret", application.ClientSecret },
                { "scope", scope },
                { "resource", AuthenticationSettings.Resource },
            });
            using var authClient = new HttpClient(_fixture.TestFhirServer.CreateMessageHandler());
            using HttpResponseMessage tokenResponse = await authClient.PostAsync(_fixture.TestFhirServer.TokenUri, content);
            string tokenBody = await tokenResponse.Content.ReadAsStringAsync();
            Assert.True(tokenResponse.IsSuccessStatusCode, $"{tokenResponse.StatusCode}: {tokenBody}");
            using JsonDocument token = JsonDocument.Parse(tokenBody);

            var client = new HttpClient(_fixture.TestFhirServer.CreateMessageHandler())
            {
                BaseAddress = _fixture.TestFhirServer.BaseAddress,
            };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.RootElement.GetProperty("access_token").GetString());
            return client;
        }

        public class StartupWithLongResourceIds : StartupBaseForCustomProviders
        {
            public StartupWithLongResourceIds(IConfiguration configuration)
                : base(configuration)
            {
            }

            public override void ConfigureServices(IServiceCollection services)
            {
                Configuration["FhirServer:CoreFeatures:UseLongResourceIds"] = "true";
                base.ConfigureServices(services);
            }
        }

        private sealed class TrackedResources
        {
            private readonly List<Resource> _created = new List<Resource>();

            public TrackedResources(TestFhirClient adminClient)
            {
                AdminClient = adminClient;
            }

            public TestFhirClient AdminClient { get; }

            public async Task<T> CreateAsync<T>(T resource)
                where T : Resource
            {
                using FhirResponse<T> response = await AdminClient.CreateAsync(resource);
                _created.Add(response.Resource);
                return response.Resource;
            }

            public async Task UpsertAsync<T>(T resource)
                where T : Resource
            {
                using FhirResponse<T> response = await AdminClient.UpdateAsync(resource);
                Assert.Equal(HttpStatusCode.Created, response.StatusCode);
                _created.Add(response.Resource);
            }

            public async Task<(HttpStatusCode Status, string Body)> SendAsync(HttpMethod method, string uri, Resource resource = null)
            {
                (HttpStatusCode Status, string Body) response = await SendRawAsync(AdminClient.HttpClient, method, uri, resource);
                if (response.Status == HttpStatusCode.Created)
                {
                    _created.Add(new FhirJsonParser().Parse<Resource>(response.Body));
                }

                return response;
            }

            public async Task RunAsync(Func<Task> test)
            {
                var failures = new List<Exception>();
                Exception testFailure = await Record.ExceptionAsync(test);
                if (testFailure != null)
                {
                    failures.Add(testFailure);
                }

                foreach (Resource resource in _created.AsEnumerable().Reverse())
                {
                    Exception cleanupFailure = await Record.ExceptionAsync(() => AdminClient.HardDeleteAsync(resource));
                    if (cleanupFailure != null)
                    {
                        failures.Add(cleanupFailure);
                    }
                }

                if (failures.Count == 1)
                {
                    ExceptionDispatchInfo.Capture(failures[0]).Throw();
                }

                if (failures.Count > 1)
                {
                    throw new AggregateException("Test and/or resource cleanup failed.", failures);
                }
            }
        }
    }
}
