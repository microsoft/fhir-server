// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Serialization;
using Microsoft.IdentityModel.JsonWebTokens;
using Task = System.Threading.Tasks.Task;
#if !R5
using RestfulCapabilityMode = Hl7.Fhir.Model.CapabilityStatement.RestfulCapabilityMode;
#endif

namespace Microsoft.Health.Fhir.Client
{
    public class FhirClient
    {
        private const string SmartOAuthUriExtension = "http://fhir-registry.smarthealthit.org/StructureDefinition/oauth-uris";
        private const string SmartOAuthUriExtensionToken = "token";
        private const string SmartOAuthUriExtensionAuthorize = "authorize";

        private const string IfNoneExistHeaderName = "If-None-Exist";
        private const string IfMatchHeaderName = "If-Match";

        private readonly string _contentType;

        private readonly Func<Base, SummaryType, string> _serialize;
        private readonly Func<string, Resource> _deserialize;
        private readonly MediaTypeWithQualityHeaderValue _mediaType;

        /// <summary>
        /// Initializes a new instance of the <see cref="FhirClient"/> class.
        /// </summary>
        /// <param name="baseAddress">The address of the FHIR server to communicate with.</param>
        /// <param name="format">The format to communicate with the FHIR server.</param>
        /// <exception cref="InvalidOperationException">Returned if the format specified is invalid.</exception>
        public FhirClient(Uri baseAddress, ResourceFormat format)
            : this(new HttpClient { BaseAddress = baseAddress }, format)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="FhirClient"/> class.
        /// </summary>
        /// <param name="httpClient">The HttpClient to use for communication. It must have a BaseAddress specified.</param>
        /// <param name="format">The format to communicate with the FHIR server.</param>
        /// <exception cref="InvalidOperationException">Returned if the format specified is invalid.</exception>
        public FhirClient(
            HttpClient httpClient,
            ResourceFormat format)
        {
            EnsureArg.IsNotNull(httpClient, nameof(httpClient));

            if (httpClient.BaseAddress == null)
            {
                throw new ArgumentException(Resources.BaseAddressMustBeSpecified);
            }

            HttpClient = httpClient;
            Format = format;

            if (format == ResourceFormat.Json)
            {
                var jsonSerializer = new FhirJsonSerializer();

                _serialize = (resource, summary) => jsonSerializer.SerializeToString(resource, summary);

                var jsonParser = new FhirJsonParser();

                _deserialize = jsonParser.Parse<Resource>;

                _contentType = ContentType.JSON_CONTENT_HEADER;
            }
            else if (format == ResourceFormat.Xml)
            {
                var xmlSerializer = new FhirXmlSerializer();

                _serialize = (resource, summary) => xmlSerializer.SerializeToString(resource, summary);

                var xmlParser = new FhirXmlParser();

                _deserialize = xmlParser.Parse<Resource>;

                _contentType = ContentType.XML_CONTENT_HEADER;
            }
            else
            {
                throw new InvalidOperationException("Unsupported format.");
            }

            _mediaType = MediaTypeWithQualityHeaderValue.Parse(_contentType);
        }

        public ResourceFormat Format { get; }

        /// <summary>
        /// Value representing if the FHIR server has security options present in the metadata.
        /// <remarks><value>null</value> indicates that the <see cref="ConfigureSecurityOptions"/> method has not been called.</remarks>
        /// </summary>
        public bool? SecurityEnabled { get; private set; }

        public Uri AuthorizeUri { get; private set; }

        public Uri TokenUri { get; private set; }

        public HttpClient HttpClient { get; }

        public DateTime TokenExpiration { get; private set; }

        public void SetBearerToken(string token)
        {
            EnsureArg.IsNotNullOrWhiteSpace(token, nameof(token));

            var decodedToken = new JsonWebToken(token);
            TokenExpiration = decodedToken.ValidTo;

            HttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        public Task<FhirResponse<T>> CreateAsync<T>(T resource, string conditionalCreateCriteria = null)
            where T : Resource
        {
            return CreateAsync(resource.ResourceType.ToString(), resource, conditionalCreateCriteria);
        }

        public async Task<FhirResponse<T>> CreateAsync<T>(string uri, T resource, string conditionalCreateCriteria = null)
            where T : Resource
        {
            var message = new HttpRequestMessage(HttpMethod.Post, uri);
            message.Headers.Accept.Add(_mediaType);
            message.Content = CreateStringContent(resource);

            if (!string.IsNullOrEmpty(conditionalCreateCriteria))
            {
                message.Headers.TryAddWithoutValidation(IfNoneExistHeaderName, conditionalCreateCriteria);
            }

            using HttpResponseMessage response = await HttpClient.SendAsync(message);

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

        public Task<FhirResponse<T>> ConditionalUpdateAsync<T>(T resource, string searchCriteria, string ifMatchVersion = null)
            where T : Resource
        {
            return UpdateAsync($"{resource.ResourceType}?{searchCriteria}", resource, ifMatchVersion);
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
                var weakETag = $"W/\"{ifMatchVersion}\"";

                request.Headers.Add(IfMatchHeaderName, weakETag);
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

        public async Task<FhirResponse> PatchAsync(string uri, string content)
        {
            var message = new HttpRequestMessage(HttpMethod.Patch, uri)
            {
                Content = new StringContent(content),
            };
            message.Headers.Accept.Add(_mediaType);

            HttpResponseMessage response = await HttpClient.SendAsync(message);

            await EnsureSuccessStatusCodeAsync(response);

            return new FhirResponse(response);
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

        public async Task<Uri> ExportAsync()
        {
            var message = new HttpRequestMessage(HttpMethod.Get, "$export");

            message.Headers.Add("Accept", "application/fhir+json");
            message.Headers.Add("Prefer", "respond-async");

            HttpResponseMessage response = await HttpClient.SendAsync(message);

            await EnsureSuccessStatusCodeAsync(response);

            return response.Content.Headers.ContentLocation;
        }

        public async Task<HttpResponseMessage> CheckExportAsync(Uri contentLocation)
        {
            var message = new HttpRequestMessage(HttpMethod.Get, contentLocation);

            HttpResponseMessage response = await HttpClient.SendAsync(message);

            return response;
        }

        public async Task<FhirResponse<Bundle>> PostBundleAsync(Resource bundle)
        {
            var message = new HttpRequestMessage(HttpMethod.Post, string.Empty)
            {
                Content = CreateStringContent(bundle),
            };

            message.Headers.Accept.Add(_mediaType);

            HttpResponseMessage response = await HttpClient.SendAsync(message);

            await EnsureSuccessStatusCodeAsync(response);

            return await CreateResponseAsync<Bundle>(response);
        }

        /// <summary>
        /// Calls the $validate endpoint.
        /// </summary>
        /// <param name="uri">The URL to call</param>
        /// <param name="resource">The resource to be validated. The resource parameter is a string instead of a Resource object because the validate endpoint is frequently sent invalid resources that couldn't be parsed.</param>
        /// <param name="xml">Whether the resource is in JSON or XML formal</param>
        public async Task<OperationOutcome> ValidateAsync(string uri, string resource, bool xml = false)
        {
            var message = new HttpRequestMessage(HttpMethod.Post, xml ? uri + "?_format=xml" : uri);
            message.Content = new StringContent(resource, Encoding.UTF8, xml ? ContentType.XML_CONTENT_HEADER : ContentType.JSON_CONTENT_HEADER);

            HttpResponseMessage response = await HttpClient.SendAsync(message);

            await EnsureSuccessStatusCodeAsync(response);

            return await CreateResponseAsync<OperationOutcome>(response);
        }

        private StringContent CreateStringContent(Resource resource)
        {
            return new StringContent(_serialize(resource, SummaryType.False), Encoding.UTF8, _contentType);
        }

        private async Task EnsureSuccessStatusCodeAsync(HttpResponseMessage response)
        {
            if (!response.IsSuccessStatusCode)
            {
                await response.Content.LoadIntoBufferAsync();

                FhirResponse<OperationOutcome> operationOutcome;
                try
                {
                    operationOutcome = await CreateResponseAsync<OperationOutcome>(response);
                }
                catch (Exception)
                {
                    // The response could not be read as an OperationOutcome. Throw a generic HTTP error.
                    throw new HttpRequestException($"Status code: {response.StatusCode}; reason phrase: '{response.ReasonPhrase}'; body: '{await response.Content.ReadAsStringAsync()}'");
                }

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

        /// <summary>
        /// Set the security options on the <see cref="FhirClient"/>.
        /// <remarks>Examines the metadata endpoint to determine if there's a token and authorize url exposed and sets the property <see cref="SecurityEnabled"/> to <value>true</value> or <value>false</value> based on this.
        /// Additionally, the <see cref="TokenUri"/> and <see cref="AuthorizeUri"/> are set if they are found.</remarks>
        /// </summary>
        public void ConfigureSecurityOptions()
        {
            bool localSecurityEnabled = false;

            using FhirResponse<CapabilityStatement> readResponse = ReadAsync<CapabilityStatement>("metadata").GetAwaiter().GetResult();
            CapabilityStatement metadata = readResponse.Resource;

            foreach (var rest in metadata.Rest.Where(r => r.Mode == RestfulCapabilityMode.Server))
            {
                var oauth = rest.Security?.GetExtension(SmartOAuthUriExtension);
                if (oauth != null)
                {
                    var tokenUrl = oauth.GetExtensionValue<FhirUri>(SmartOAuthUriExtensionToken).Value;
                    var authorizeUrl = oauth.GetExtensionValue<FhirUri>(SmartOAuthUriExtensionAuthorize).Value;

                    localSecurityEnabled = true;
                    TokenUri = new Uri(tokenUrl);
                    AuthorizeUri = new Uri(authorizeUrl);

                    break;
                }
            }

            SecurityEnabled = localSecurityEnabled;
        }
    }
}
