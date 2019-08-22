// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Validation;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Xunit;
using FhirClient = Microsoft.Health.Fhir.Tests.E2E.Common.FhirClient;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.All)]
    public class CreateTests : IClassFixture<HttpIntegrationTestFixture>
    {
        public CreateTests(HttpIntegrationTestFixture fixture)
        {
            Client = fixture.FhirClient;
        }

        protected FhirClient Client { get; set; }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task WhenPostingToHttp_GivenAResource_TheServerShouldRespondSuccessfully()
        {
            FhirResponse<Observation> response = await Client.CreateAsync(Samples.GetDefaultObservation().ToPoco<Observation>());

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.NotNull(response.Headers.ETag);
            Assert.NotNull(response.Headers.Location);
            Assert.NotNull(response.Content.Headers.LastModified);

            Observation observation = response.Resource;

            Assert.NotNull(observation.Id);
            Assert.NotNull(observation.Meta.VersionId);
            Assert.NotNull(observation.Meta.LastUpdated);

            Assert.Equal($@"W/""{observation.Meta.VersionId}""", response.Headers.ETag.ToString());

            TestHelper.AssertLocationHeaderIsCorrect(Client, observation, response.Headers.Location);
            TestHelper.AssertLastUpdatedAndLastModifiedAreEqual(observation.Meta.LastUpdated, response.Content.Headers.LastModified);

            DotNetAttributeValidation.Validate(observation, true);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task WhenPostingToHttp_GivenAResourceWithIdAndMeta_TheServerShouldRespondSuccessfullyWithUpdatedContents()
        {
            Observation originalResource = Samples.GetDefaultObservation().ToPoco<Observation>();
            originalResource.Id = Guid.NewGuid().ToString();
            originalResource.Meta = new Meta
            {
                VersionId = Guid.NewGuid().ToString(),
                LastUpdated = DateTimeOffset.UtcNow,
            };

            FhirResponse<Observation> response = await Client.CreateAsync(originalResource);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.NotNull(response.Headers.ETag);
            Assert.NotNull(response.Headers.Location);
            Assert.NotNull(response.Content.Headers.LastModified);

            Observation observation = response.Resource;

            Assert.NotNull(observation.Id);
            Assert.NotNull(observation.Meta.VersionId);
            Assert.NotNull(observation.Meta.LastUpdated);

            Assert.NotEqual(originalResource.Id, observation.Id);
            Assert.NotEqual(originalResource.Meta.LastUpdated, observation.Meta.LastUpdated);
            Assert.NotEqual(originalResource.Meta.VersionId, observation.Meta.VersionId);

            TestHelper.AssertLocationHeaderIsCorrect(Client, observation, response.Headers.Location);
            TestHelper.AssertLastUpdatedAndLastModifiedAreEqual(observation.Meta.LastUpdated, response.Content.Headers.LastModified);

            DotNetAttributeValidation.Validate(observation, true);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task WhenPostingToHttp_GivenALocationResourceWithPosition_TheServerShouldRespondSuccessfully()
        {
            Location originalResource = Samples.GetJsonSample("Location-example-hq").ToPoco<Location>();

            FhirResponse<Location> response = await Client.CreateAsync(originalResource);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task WhenPostingToHttp_GivenAnUnsupportedResourceType_TheServerShouldRespondWithANotFoundResponse()
        {
            FhirException ex = await Assert.ThrowsAsync<FhirException>(() => Client.CreateAsync("NotObservation", Samples.GetDefaultObservation().ToPoco<Observation>()));

            Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task WhenPostingToHttp_GivenUnsetContentType_TheServerShouldRespondWithAUnsupportedMediaTypeResponse()
        {
            var result = await Client.
                HttpClient.PostAsync("Observation", new StringContent("Content!"));

            Assert.Equal(HttpStatusCode.UnsupportedMediaType, result.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task WhenPostingToHttp_GivenAnUnsupportedContentType_TheServerShouldRespondWithAUnsupportedMediaTypeResponse()
        {
            var result = await Client.HttpClient.PostAsync("Observation", new FormUrlEncodedContent(new KeyValuePair<string, string>[0]));

            Assert.Equal(HttpStatusCode.UnsupportedMediaType, result.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task WhenPostingToHttp_GivenAnInvalidResource_TheServerShouldRespondWithBadRequestResponse()
        {
            // An empty observation is invalid because it is missing fields that have a minimum cardinality of 1
            FhirException ex = await Assert.ThrowsAsync<FhirException>(() => Client.CreateAsync(new Observation()));

            Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        }

        [Theory]
        [MemberData(nameof(AllXssStrings))]
        [Trait(Traits.Priority, Priority.One)]
        public async Task WhenPostingToHttpWithMaliciousUrl_GivenAResource_TheServerShouldHandleRequest(string code)
        {
            FhirResponse<Observation> response = await Client.CreateAsync($"Observation?{code}", Samples.GetDefaultObservation().ToPoco<Observation>());

            // Status should always be created in these tests
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.NotNull(response.Headers.ETag);
            Assert.NotNull(response.Headers.Location);
            Assert.NotNull(response.Content.Headers.LastModified);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task WhenPostingToHttpWithMaliciousId_GivenAResource_TheServerShouldThrowBadRequestOrNotFound()
        {
            var observation = Samples.GetDefaultObservation()
                .UpdateId("' SELECT name FROM syscolumns WHERE id = (SELECT id FROM sysobjects WHERE name = tablename')--")
                .ToPoco<Observation>();

            var exception = await Assert.ThrowsAsync<FhirException>(async () => await Client.UpdateAsync(observation));

            Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        }

        [Theory]
        [MemberData(nameof(HandledXssStrings))]
        [Trait(Traits.Priority, Priority.One)]
        public async Task WhenPostingToHttpWithMaliciousNarrative_GivenAResource_TheServerShouldHandleRequest(string code)
        {
            var observation = Samples.GetDefaultObservation().ToPoco<Observation>();
            observation.Text = new Narrative
            {
                Status = Narrative.NarrativeStatus.Generated,
                Div = $"<div>{code}</div>",
            };

            FhirResponse<Observation> response = await Client.CreateAsync(observation);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.NotNull(response.Headers.ETag);
            Assert.NotNull(response.Headers.Location);
            Assert.NotNull(response.Content.Headers.LastModified);
        }

        [Theory]
        [MemberData(nameof(BadRequestXssStrings))]
        [Trait(Traits.Priority, Priority.One)]
        public async Task WhenPostingToHttpWithMaliciousNarrative_GivenAResource_TheServerShouldBlockRequest(string code)
        {
            var observation = Samples.GetDefaultObservation().ToPoco<Observation>();
            observation.Text = new Narrative
            {
                Status = Narrative.NarrativeStatus.Generated,
                Div = $"<div>{code}</div>",
            };

            // Xml can't even serialize these broken fragments
            if (Client.Format != ResourceFormat.Xml)
            {
                var exception = await Assert.ThrowsAsync<FhirException>(() => Client.CreateAsync(observation));
                Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
            }
        }

        /// <summary>
        /// Malicious Url Data Source
        /// </summary>
        /// <remarks>
        /// These examples from https://www.owasp.org/index.php/OWASP_Testing_Guide_Appendix_C:_Fuzz_Vectors
        /// </remarks>
        public static IEnumerable<object[]> HandledXssStrings()
        {
            return new List<object>
            {
                "%3cscript src=http://www.example.com/malicious-code.js%3e%3c/script%3e",
                "\\x3cscript src=http://www.example.com/malicious-code.js\\x3e\\x3c/script\\x3e",
                "'%uff1cscript%uff1ealert('XSS')%uff1c/script%uff1e'",
                "' having 1=1--",
                "' SELECT name FROM syscolumns WHERE id = (SELECT id FROM sysobjects WHERE name = tablename')--",
                "'+or+'1'='1",
            }.Select(x => new[] { x });
        }

        public static IEnumerable<object[]> BadRequestXssStrings()
        {
            return new List<object>
            {
                // Any string with a tag should result in a bad request
                "<script src=http://www.example.com/malicious-code.js></script>",
                "http://www.example.com/>\"><script>alert(\"XSS\")</script>&",
                "<img%20src%3D%26%23x6a;%26%23x61;%26%23x76;%26%23x61;%26%23x73;%26%23x63;%26%23x72;%26%23x69;%26%23x70;%26%23x74;%26%23x3a;alert(%26quot;%26%23x20;XSS%26%23x20;Test%26%23x20;Successful%26quot;)>",
                ">%22%27><img%20src%3d%22javascript:alert(%27%20XSS%27)%22>",
                "<IMG SRC=\"jav &#x0D;ascript:alert(<WBR>'XSS');\">",
                "<?xml version=\"1.0\" encoding=\"ISO-8859-1\"?><foo><![CDATA[<]]>SCRIPT<![CDATA[>]]>alert('gotcha');<![CDATA[<]]>/SCRIPT<![CDATA[>]]></foo>",
            }.Select(x => new[] { x });
        }

        public static IEnumerable<object[]> AllXssStrings()
        {
            return HandledXssStrings().Concat(BadRequestXssStrings());
        }
    }
}
