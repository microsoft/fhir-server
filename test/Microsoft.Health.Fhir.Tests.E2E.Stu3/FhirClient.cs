// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Serialization;
using Hl7.Fhir.Validation;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Net.Http.Headers;
using Newtonsoft.Json.Linq;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Stu3
{
    public class FhirClient : ICustomFhirClient
    {
        private readonly Format _format;
        private readonly Dictionary<string, string> _bearerTokens = new Dictionary<string, string>();
        private readonly string _contentType;

        private readonly BaseFhirSerializer _serializer;
        private readonly BaseFhirParser _parser;

        private readonly Func<Base, SummaryType, string> _serialize;
        private readonly Func<string, Resource> _deserialize;
        private readonly MediaTypeWithQualityHeaderValue _mediaType;

        public FhirClient(HttpClient httpClient, Format format)
        {
            HttpClient = httpClient;

            _format = format;

            if (format == Tests.Common.FixtureParameters.Format.Json)
            {
                var jsonSerializer = new FhirJsonSerializer();

                _serializer = jsonSerializer;
                _serialize = (resource, summary) => jsonSerializer.SerializeToString(resource, summary);

                var jsonParser = new FhirJsonParser();

                _parser = jsonParser;
                _deserialize = jsonParser.Parse<Resource>;

                _contentType = ContentType.JSON_CONTENT_HEADER;
            }
            else if (format == Tests.Common.FixtureParameters.Format.Xml)
            {
                var xmlSerializer = new FhirXmlSerializer();

                _serializer = xmlSerializer;
                _serialize = (resource, summary) => xmlSerializer.SerializeToString(resource, summary);

                var xmlParser = new FhirXmlParser();

                _parser = xmlParser;
                _deserialize = xmlParser.Parse<Resource>;

                _contentType = ContentType.XML_CONTENT_HEADER;
            }
            else
            {
                throw new InvalidOperationException("Unsupported format.");
            }

            _mediaType = MediaTypeWithQualityHeaderValue.Parse(_contentType);
            SetupAuthenticationAsync(TestApplications.ServiceClient).GetAwaiter().GetResult();
        }

        public Format Format => _format;

        public (bool SecurityEnabled, string AuthorizeUrl, string TokenUrl) SecuritySettings { get; private set; }

        public HttpClient HttpClient { get; }

        public async Task RunAsUser(TestUser user, TestApplication clientApplication)
        {
            EnsureArg.IsNotNull(user, nameof(user));
            EnsureArg.IsNotNull(clientApplication, nameof(clientApplication));
            await SetupAuthenticationAsync(clientApplication, user);
        }

        public async Task RunAsClientApplication(TestApplication clientApplication)
        {
            EnsureArg.IsNotNull(clientApplication, nameof(clientApplication));
            await SetupAuthenticationAsync(clientApplication);
        }

        public Task<FhirResponse<T>> CreateAsync<T>(T resource)
            where T : ResourceElement
        {
            return CreateAsync(resource.InstanceType, resource);
        }

        public async Task<FhirResponse<T>> CreateAsync<T>(string uri, T resource)
            where T : ResourceElement
        {
            var message = new HttpRequestMessage(HttpMethod.Post, uri);
            message.Headers.Accept.Add(_mediaType);
            message.Content = CreateStringContent(resource);

            HttpResponseMessage response = await HttpClient.SendAsync(message);

            await EnsureSuccessStatusCodeAsync(response);

            return await CreateResourceElementResponseAsync<T>(response);
        }

        public Task<FhirResponse<T>> ReadAsync<T>(string resourceType, string resourceId)
            where T : ResourceElement
        {
            return ReadAsync<T>($"{resourceType}/{resourceId}");
        }

        public async Task<FhirResponse<T>> ReadAsync<T>(string uri)
            where T : ResourceElement
        {
            var message = new HttpRequestMessage(HttpMethod.Get, uri);
            message.Headers.Accept.Add(_mediaType);

            HttpResponseMessage response = await HttpClient.SendAsync(message);

            await EnsureSuccessStatusCodeAsync(response);

            return await CreateResourceElementResponseAsync<T>(response);
        }

        public Task<FhirResponse<T>> VReadAsync<T>(string resourceType, string resourceId, string versionId)
            where T : ResourceElement
        {
            return ReadAsync<T>($"{resourceType}/{resourceId}/_history/{versionId}");
        }

        public Task<FhirResponse<T>> UpdateAsync<T>(T resource, string ifMatchVersion = null)
            where T : ResourceElement
        {
            return UpdateAsync($"{resource.InstanceType}/{resource.Id}", resource, ifMatchVersion);
        }

        public async Task<FhirResponse<T>> UpdateAsync<T>(string uri, T resource, string ifMatchVersion = null)
            where T : ResourceElement
        {
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Put, uri)
            {
                Content = CreateStringContent(resource),
            };
            request.Headers.Accept.Add(_mediaType);

            if (ifMatchVersion != null)
            {
                WeakETag weakETag = WeakETag.FromVersionId(ifMatchVersion);

                request.Headers.Add(HeaderNames.IfMatch, weakETag.ToString());
            }

            HttpResponseMessage response = await HttpClient.SendAsync(request);

            await EnsureSuccessStatusCodeAsync(response);

            return await CreateResourceElementResponseAsync<T>(response);
        }

        public Task<FhirResponse> DeleteAsync<T>(T resource)
            where T : ResourceElement
        {
            return DeleteAsync($"{resource.InstanceType}/{resource.Id}");
        }

        public async Task<FhirResponse> DeleteAsync(string uri)
        {
            var message = new HttpRequestMessage(HttpMethod.Delete, uri);
            message.Headers.Accept.Add(_mediaType);

            HttpResponseMessage response = await HttpClient.SendAsync(message);

            await EnsureSuccessStatusCodeAsync(response);

            return new FhirResponse(response);
        }

        public Task<FhirResponse> HardDeleteAsync<T>(T resource)
            where T : ResourceElement
        {
            return DeleteAsync($"{resource.InstanceType}/{resource.Id}?hardDelete=true");
        }

        public Task<FhirResponse<ResourceElement>> SearchAsync(string resourceType, string query = null, int? count = null)
        {
            StringBuilder sb = new StringBuilder();

            sb.Append(resourceType).Append("?");

            if (query != null)
            {
                sb.Append(query);
            }

            if (count != null)
            {
                if (sb[sb.Length - 1] != '?')
                {
                    sb.Append("&");
                }

                sb.Append("_count=").Append(count.Value);
            }

            return SearchAsync(sb.ToString());
        }

        public async Task<FhirResponse<ResourceElement>> SearchAsync(string url)
        {
            var message = new HttpRequestMessage(HttpMethod.Get, url);
            message.Headers.Accept.Add(_mediaType);

            HttpResponseMessage response = await HttpClient.SendAsync(message);

            await EnsureSuccessStatusCodeAsync(response);

            return await CreateResourceElementResponseAsync<ResourceElement>(response);
        }

        public async Task<FhirResponse<ResourceElement>> SearchPostAsync(string resourceType, params (string key, string value)[] body)
        {
            var message = new HttpRequestMessage(HttpMethod.Post, $"{(string.IsNullOrEmpty(resourceType) ? null : $"{resourceType}/")}_search")
            {
                Content = new FormUrlEncodedContent(body.ToDictionary(p => p.key, p => p.value)),
            };
            HttpResponseMessage response = await HttpClient.SendAsync(message);

            await EnsureSuccessStatusCodeAsync(response);

            return await CreateResourceElementResponseAsync<ResourceElement>(response);
        }

        public async Task<string> ExportAsync(Dictionary<string, string> queryParams)
        {
            string path = QueryHelpers.AddQueryString("$export", queryParams);
            var message = new HttpRequestMessage(HttpMethod.Get, path);

            message.Headers.Add("Accept", "application/fhir+json");
            message.Headers.Add("Prefer", "respond-async");

            HttpResponseMessage response = await HttpClient.SendAsync(message);

            await EnsureSuccessStatusCodeAsync(response);

            IEnumerable<string> contentLocation = response.Content.Headers.GetValues(HeaderNames.ContentLocation);

            return contentLocation.First();
        }

        public StringContent CreateStringContent(ResourceElement resource)
        {
            return new StringContent(_serialize(resource.ToPoco(), SummaryType.False), Encoding.UTF8, _contentType);
        }

        public async Task EnsureSuccessStatusCodeAsync(HttpResponseMessage response)
        {
            if (!response.IsSuccessStatusCode)
            {
                FhirResponse<ResourceElement> operationOutcome = await CreateResourceElementResponseAsync<ResourceElement>(response);

                throw new FhirException(operationOutcome);
            }
        }

        public async Task<FhirResponse<T>> CreateResourceElementResponseAsync<T>(HttpResponseMessage response)
            where T : ResourceElement
        {
            string content = await response.Content.ReadAsStringAsync();

            var resource = _deserialize(content).ToResourceElement();

            return new FhirResponse<T>(
                response,
                string.IsNullOrWhiteSpace(content) ? null : (T)resource);
        }

        public async Task<FhirResponse<T>> CreateResponseAsync<T>(HttpResponseMessage response)
            where T : Resource
        {
            string content = await response.Content.ReadAsStringAsync();

            return new FhirResponse<T>(
                response,
                string.IsNullOrWhiteSpace(content) ? null : (T)_deserialize(content));
        }

        public async Task SetupAuthenticationAsync(TestApplication clientApplication, TestUser user = null)
        {
            await GetSecuritySettings("metadata");

            if (SecuritySettings.SecurityEnabled)
            {
                var tokenKey = $"{clientApplication.ClientId}:{(user == null ? string.Empty : user.UserId)}";

                if (!_bearerTokens.TryGetValue(tokenKey, out string bearerToken))
                {
                    bearerToken = await GetBearerToken(clientApplication, user);
                    _bearerTokens[tokenKey] = bearerToken;
                }

                HttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

                // TODO: Fix this
                ////HttpClient.SetBearerToken(bearerToken);
            }
        }

        public async Task<string> GetBearerToken(TestApplication clientApplication, TestUser user)
        {
            if (clientApplication.Equals(TestApplications.InvalidClient))
            {
                return null;
            }

            var formContent = new FormUrlEncodedContent(user == null ? GetAppSecuritySettings(clientApplication) : GetUserSecuritySettings(clientApplication, user));

            HttpResponseMessage tokenResponse = await HttpClient.PostAsync(SecuritySettings.TokenUrl, formContent);

            var tokenJson = JObject.Parse(await tokenResponse.Content.ReadAsStringAsync());

            var bearerToken = tokenJson["access_token"].Value<string>();

            return bearerToken;
        }

        public List<KeyValuePair<string, string>> GetAppSecuritySettings(TestApplication clientApplication)
        {
            string scope = clientApplication == TestApplications.WrongAudienceClient ? clientApplication.ClientId : AuthenticationSettings.Scope;
            string resource = clientApplication == TestApplications.WrongAudienceClient ? clientApplication.ClientId : AuthenticationSettings.Resource;

            return new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("client_id", clientApplication.ClientId),
                new KeyValuePair<string, string>("client_secret", clientApplication.ClientSecret),
                new KeyValuePair<string, string>("grant_type", clientApplication.GrantType),
                new KeyValuePair<string, string>("scope", scope),
                new KeyValuePair<string, string>("resource", resource),
            };
        }

        public List<KeyValuePair<string, string>> GetUserSecuritySettings(TestApplication clientApplication, TestUser user)
        {
            return new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("client_id", clientApplication.ClientId),
                new KeyValuePair<string, string>("client_secret", clientApplication.ClientSecret),
                new KeyValuePair<string, string>("grant_type", user.GrantType),
                new KeyValuePair<string, string>("scope", AuthenticationSettings.Scope),
                new KeyValuePair<string, string>("resource", AuthenticationSettings.Resource),
                new KeyValuePair<string, string>("username", user.UserId),
                new KeyValuePair<string, string>("password", user.Password),
            };
        }

        public async Task GetSecuritySettings(string fhirServerMetadataUrl)
        {
            FhirResponse<ResourceElement> readResponse = await ReadAsync<ResourceElement>(fhirServerMetadataUrl);

            var metadata = readResponse.Resource.ToPoco<CapabilityStatement>();

            foreach (var rest in metadata.Rest.Where(r => r.Mode == CapabilityStatement.RestfulCapabilityMode.Server))
            {
                var oauth = rest.Security?.GetExtension(Constants.SmartOAuthUriExtension);
                if (oauth != null)
                {
                    var tokenUrl = oauth.GetExtensionValue<FhirUri>(Constants.SmartOAuthUriExtensionToken).Value;
                    var authorizeUrl = oauth.GetExtensionValue<FhirUri>(Constants.SmartOAuthUriExtensionAuthorize).Value;

                    SecuritySettings = (true, authorizeUrl, tokenUrl);
                    return;
                }
            }

            SecuritySettings = (false, null, null);
        }

        public ResourceElement GetDefaultObservation()
        {
            return GetJsonSample("Weight");
        }

        public ResourceElement GetDefaultPatient()
        {
            return GetJsonSample("Patient");
        }

        public ResourceElement GetDefaultOrganization()
        {
            return GetJsonSample("Organization");
        }

        public ResourceElement GetEmptyObservation()
        {
            return new Observation().ToTypedElement().ToResourceElement();
        }

        /// <summary>
        /// Gets back a resource from a json sample file.
        /// </summary>
        /// <param name="fileName">The JSON filename, omit the extension</param>
        public ResourceElement GetJsonSample(string fileName)
        {
            var json = EmbeddedResourceManager.GetStringContent("TestFiles", fileName, "json", "Stu3");
            var parser = new Hl7.Fhir.Serialization.FhirJsonParser();
            return parser.Parse<Resource>(json).ToTypedElement().ToResourceElement();
        }

        public void Validate(ResourceElement resourceElement)
        {
            DotNetAttributeValidation.Validate(resourceElement.ToPoco(), true);
        }

        public ResourceElement UpdateId(ResourceElement resourceElement, string id)
        {
            return resourceElement.UpdateId(id);
        }

        public ResourceElement UpdateVersion(ResourceElement resourceElement, string newVersion)
        {
            return resourceElement.UpdateVersion(newVersion);
        }

        public ResourceElement UpdateLastUpdated(ResourceElement resourceElement, DateTimeOffset lastUpdated)
        {
            return resourceElement.UpdateLastUpdated(lastUpdated);
        }

        public ResourceElement UpdateText(ResourceElement resourceElement, string text)
        {
            return resourceElement.UpdateText(text);
        }

        public ResourceElement UpdatePatientFamilyName(ResourceElement resourceElement, string familyName)
        {
            return resourceElement.UpdatePatientFamilyName(familyName);
        }

        public ResourceElement UpdatePatientAddressCity(ResourceElement resourceElement, string city)
        {
            return resourceElement.UpdatePatientAddressCity(city);
        }

        public ResourceElement UpdatePatientGender(ResourceElement resourceElement, string gender)
        {
            return resourceElement.UpdatePatientGender(gender);
        }

        public ResourceElement UpdateObservationStatus(ResourceElement resourceElement, string status)
        {
            return resourceElement.UpdateObservationStatus(status);
        }

        public ResourceElement AddObservationCoding(ResourceElement resourceElement, string system, string code)
        {
            return resourceElement.AddObservationCoding(system, code);
        }

        public ResourceElement AddMetaTag(ResourceElement resourceElement, string system, string code)
        {
            return resourceElement.AddMetaTag(system, code);
        }

        public bool Compare(ResourceElement expected, ITypedElement actual)
        {
            var expectedResource = expected.ToPoco();
            var actualResource = actual.ToPoco();

            return expectedResource.IsExactly(actualResource);
        }

        public bool Compare(ResourceElement expected, ResourceElement actual)
        {
            var expectedResource = expected.ToPoco();
            var actualResource = actual.ToPoco();

            return expectedResource.IsExactly(actualResource);
        }
    }
}
