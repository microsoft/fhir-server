// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Serialization;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.DataConvert;
using Microsoft.Health.Fhir.Core.Features.Operations.DataConvert.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Fhir.Tests.E2E.Rest;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Shared.Tests.E2E.Rest
{
    [Trait(Traits.Category, Categories.DataConvert)]
    [HttpIntegrationFixtureArgumentSets(DataStore.CosmosDb, Format.Json)]
    public class DataConvertTests : IClassFixture<HttpIntegrationTestFixture>
    {
        private bool _isUsingInProcTestServer = false;
        private readonly TestFhirClient _testFhirClient;
        private readonly IContainerRegistryTokenProvider _containerRegistryTokenProvider;
        private readonly DataConvertConfiguration _dataConvertConfiguration;

        public DataConvertTests(HttpIntegrationTestFixture fixture)
        {
            _isUsingInProcTestServer = fixture.IsUsingInProcTestServer;
            _testFhirClient = fixture.TestFhirClient;
            _containerRegistryTokenProvider = (IContainerRegistryTokenProvider)(fixture.TestFhirServer as InProcTestFhirServer)?.Server?.Services?.GetService(typeof(IContainerRegistryTokenProvider));
            _dataConvertConfiguration = ((IOptions<DataConvertConfiguration>)(fixture.TestFhirServer as InProcTestFhirServer)?.Server?.Services?.GetService(typeof(IOptions<DataConvertConfiguration>)))?.Value;
        }

        [Fact]
        public async Task GivenAValidRequest_WhenDataConvert_CorrectResponseShouldBeReturned()
        {
            if (_isUsingInProcTestServer)
            {
                return;
            }

            var requestMessage = GenerateDataConvertRequest();
            HttpResponseMessage response = await _testFhirClient.HttpClient.SendAsync(requestMessage);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var bundleContent = await response.Content.ReadAsStringAsync();
            var parser = new FhirJsonParser();
            var bundleResource = parser.Parse<Bundle>(bundleContent);
            Assert.Equal("urn:uuid:58fbaa10-6948-35b8-b27c-cb8d36dd5926", bundleResource.Entry.First().FullUrl);
        }

        private async Task<string> GetRegistryToken()
        {
            ContainerRegistryInfo registry = _dataConvertConfiguration.ContainerRegistries.First();
            if (string.IsNullOrEmpty(registry.ContainerRegistryUsername)
                || string.IsNullOrEmpty(registry.ContainerRegistryPassword))
            {
                registry.ContainerRegistryUsername = registry.ContainerRegistryServer.Split('.')[0];
                registry.ContainerRegistryPassword = Environment.GetEnvironmentVariable(registry.ContainerRegistryUsername + "_secret");
            }

            return await _containerRegistryTokenProvider.GetTokenAsync(registry, CancellationToken.None);
        }

        private HttpRequestMessage GenerateDataConvertRequest(
            string path = "$data-convert",
            string acceptHeader = ContentType.JSON_CONTENT_HEADER,
            string preferHeader = "respond-async",
            Dictionary<string, string> queryParams = null)
        {
            var request = new HttpRequestMessage
            {
                Method = HttpMethod.Post,
            };

            request.Content = new StringContent(GetValidDataConvertParams().ToJson(), System.Text.Encoding.UTF8, "application/json");
            request.RequestUri = new Uri(_testFhirClient.HttpClient.BaseAddress, path);

            return request;
        }

        private static Parameters GetValidDataConvertParams()
        {
            var parametersResource = new Parameters();
            parametersResource.Parameter = new List<Parameters.ParameterComponent>();

            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = JobRecordProperties.InputData, Value = new FhirString(GetSampleHl7v2Message()) });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = JobRecordProperties.InputDataType, Value = new FhirString("Hl7v2") });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = JobRecordProperties.TemplateSetReference, Value = new FhirString("yufei0909acr.azurecr.io/template:default") });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = JobRecordProperties.EntryPointTemplate, Value = new FhirString("ADT_A01") });

            return parametersResource;
        }

        private static string GetSampleHl7v2Message()
        {
            return "MSH|^~\\&|SIMHOSP|SFAC|RAPP|RFAC|20200508131015||ADT^A01|517|T|2.3|||AL||44|ASCII\nEVN|A01|20200508131015|||C005^Whittingham^Sylvia^^^Dr^^^DRNBR^PRSNL^^^ORGDR|\nPID|1|3735064194^^^SIMULATOR MRN^MRN|3735064194^^^SIMULATOR MRN^MRN~2021051528^^^NHSNBR^NHSNMBR||Kinmonth^Joanna^Chelsea^^Ms^^CURRENT||19870624000000|F|||89 Transaction House^Handmaiden Street^Wembley^^FV75 4GJ^GBR^HOME||020 3614 5541^HOME|||||||||C^White - Other^^^||||||||\nPD1|||FAMILY PRACTICE^^12345|\nPV1|1|I|OtherWard^MainRoom^Bed 183^Simulated Hospital^^BED^Main Building^4|28b|||C005^Whittingham^Sylvia^^^Dr^^^DRNBR^PRSNL^^^ORGDR|||CAR|||||||||16094728916771313876^^^^visitid||||||||||||||||||||||ARRIVED|||20200508131015||";
        }
    }
}
