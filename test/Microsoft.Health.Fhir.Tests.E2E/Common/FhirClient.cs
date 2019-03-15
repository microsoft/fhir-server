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
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Net.Http.Headers;
using Newtonsoft.Json.Linq;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Common
{
    public class FhirClient
    {
        private readonly ResourceFormat _format;
        private readonly Dictionary<string, string> _bearerTokens = new Dictionary<string, string>();
        private readonly string _contentType;

        private readonly BaseFhirSerializer _serializer;
        private readonly BaseFhirParser _parser;

        private readonly Func<Base, SummaryType, string> _serialize;
        private readonly Func<string, Resource> _deserialize;
        private readonly MediaTypeWithQualityHeaderValue _mediaType;

        public FhirClient(HttpClient httpClient, ResourceFormat format)
        {
            HttpClient = httpClient;
            _format = format;

            if (format == ResourceFormat.Json)
            {
                var jsonSerializer = new FhirJsonSerializer();

                _serializer = jsonSerializer;
                _serialize = (resource, summary) => jsonSerializer.SerializeToString(resource, summary);

                var jsonParser = new FhirJsonParser();

                _parser = jsonParser;
                _deserialize = jsonParser.Parse<Resource>;

                _contentType = ContentType.JSON_CONTENT_HEADER;
            }
            else if (format == ResourceFormat.Xml)
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

        public ResourceFormat Format => _format;

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
            where T : Resource
        {
            return CreateAsync(resource.ResourceType.ToString(), resource);
        }

        public async Task<FhirResponse<T>> CreateAsync<T>(string uri, T resource)
            where T : Resource
        {
            var message = new HttpRequestMessage(HttpMethod.Post, uri);
            message.Headers.Accept.Add(_mediaType);
            message.Content = CreateStringContent(resource);

            HttpResponseMessage response = await HttpClient.SendAsync(message);

            await EnsureSuccessStatusCodeAsync(response);

            return await CreateResponseAsync<T>(response);
        }

        public Task<FhirResponse<T>> ReadAsync<T>(ResourceType resourceType, string resourceId)
            where T : Resource
        {
            return ReadAsync<T>($"{resourceType}/{resourceId}");
        }

        public async Task<FhirResponse<T>> ReadAsync<T>(string uri)
            where T : Resource
        {
            var message = new HttpRequestMessage(HttpMethod.Get, uri);
            message.Headers.Accept.Add(_mediaType);

            HttpResponseMessage response = await HttpClient.SendAsync(message);

            await EnsureSuccessStatusCodeAsync(response);

            return await CreateResponseAsync<T>(response);
        }

        public Task<FhirResponse<T>> VReadAsync<T>(ResourceType resourceType, string resourceId, string versionId)
            where T : Resource
        {
            return ReadAsync<T>($"{resourceType}/{resourceId}/_history/{versionId}");
        }

        public Task<FhirResponse<T>> UpdateAsync<T>(T resource, string ifMatchVersion = null)
            where T : Resource
        {
            return UpdateAsync($"{resource.ResourceType}/{resource.Id}", resource, ifMatchVersion);
        }

        public async Task<FhirResponse<T>> UpdateAsync<T>(string uri, T resource, string ifMatchVersion = null)
            where T : Resource
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

            return await CreateResponseAsync<T>(response);
        }

        public Task<FhirResponse> DeleteAsync<T>(T resource)
            where T : Resource
        {
            return DeleteAsync($"{resource.ResourceType}/{resource.Id}");
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
            where T : Resource
        {
            return DeleteAsync($"{resource.ResourceType}/{resource.Id}?hardDelete=true");
        }

        public Task<FhirResponse<Bundle>> SearchAsync(ResourceType resourceType, string query = null, int? count = null)
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

        public async Task<FhirResponse<Bundle>> SearchAsync(string url)
        {
            var message = new HttpRequestMessage(HttpMethod.Get, url);
            message.Headers.Accept.Add(_mediaType);

            HttpResponseMessage response = await HttpClient.SendAsync(message);

            await EnsureSuccessStatusCodeAsync(response);

            return await CreateResponseAsync<Bundle>(response);
        }

        public async Task<FhirResponse<Bundle>> SearchPostAsync(string resourceType, params (string key, string value)[] body)
        {
            var message = new HttpRequestMessage(HttpMethod.Post, $"{(string.IsNullOrEmpty(resourceType) ? null : $"{resourceType}/")}_search")
            {
                Content = new FormUrlEncodedContent(body.ToDictionary(p => p.key, p => p.value)),
            };
            HttpResponseMessage response = await HttpClient.SendAsync(message);

            await EnsureSuccessStatusCodeAsync(response);

            return await CreateResponseAsync<Bundle>(response);
        }

        private StringContent CreateStringContent(Resource resource)
        {
            return new StringContent(_serialize(resource, SummaryType.False), Encoding.UTF8, _contentType);
        }

        private async Task EnsureSuccessStatusCodeAsync(HttpResponseMessage response)
        {
            if (!response.IsSuccessStatusCode)
            {
                FhirResponse<OperationOutcome> operationOutcome = await CreateResponseAsync<OperationOutcome>(response);

                throw new FhirException(operationOutcome);
            }
        }

        private async Task<FhirResponse<T>> CreateResponseAsync<T>(HttpResponseMessage response)
            where T : Resource
        {
            string content = await response.Content.ReadAsStringAsync();

            return new FhirResponse<T>(
                response,
                string.IsNullOrWhiteSpace(content) ? null : (T)_deserialize(content));
        }

        private async Task SetupAuthenticationAsync(TestApplication clientApplication, TestUser user = null)
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

                HttpClient.SetBearerToken(bearerToken);
            }
        }

        private async Task<string> GetBearerToken(TestApplication clientApplication, TestUser user)
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

        private List<KeyValuePair<string, string>> GetAppSecuritySettings(TestApplication clientApplication)
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

        private List<KeyValuePair<string, string>> GetUserSecuritySettings(TestApplication clientApplication, TestUser user)
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

        private async Task GetSecuritySettings(string fhirServerMetadataUrl)
        {
            FhirResponse<CapabilityStatement> readResponse = await ReadAsync<CapabilityStatement>(fhirServerMetadataUrl);
            var metadata = readResponse.Resource;

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
    }
}
