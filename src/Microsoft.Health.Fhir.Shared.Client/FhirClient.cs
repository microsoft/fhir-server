// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using EnsureThat;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Serialization;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Client
{
    public class FhirClient : IFhirClient
    {
        private const string IfNoneExistHeaderName = "If-None-Exist";
        private const string ProvenanceHeader = "X-Provenance";
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
            ResourceFormat format = ResourceFormat.Json)
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

        public HttpClient HttpClient { get; }

        public Task<FhirResponse<T>> CreateAsync<T>(T resource, string conditionalCreateCriteria = null, string provenanceHeader = null, CancellationToken cancellationToken = default)
            where T : Resource
        {
            return CreateAsync(resource.ResourceType.ToString(), resource, conditionalCreateCriteria, provenanceHeader, cancellationToken);
        }

        public async Task<FhirResponse<T>> CreateAsync<T>(string uri, T resource, string conditionalCreateCriteria = null, string provenanceHeader = null, CancellationToken cancellationToken = default)
            where T : Resource
        {
            var message = new HttpRequestMessage(HttpMethod.Post, uri);
            message.Headers.Accept.Add(_mediaType);
            message.Content = CreateStringContent(resource);

            if (!string.IsNullOrEmpty(conditionalCreateCriteria))
            {
                message.Headers.TryAddWithoutValidation(IfNoneExistHeaderName, conditionalCreateCriteria);
            }

            if (!string.IsNullOrEmpty(provenanceHeader))
            {
                message.Headers.TryAddWithoutValidation(ProvenanceHeader, provenanceHeader);
            }

            using HttpResponseMessage response = await HttpClient.SendAsync(message, cancellationToken);

            await EnsureSuccessStatusCodeAsync(response);

            return await CreateResponseAsync<T>(response);
        }

        public Task<FhirResponse<T>> ReadAsync<T>(ResourceType resourceType, string resourceId, CancellationToken cancellationToken = default)
            where T : Resource
        {
            return ReadAsync<T>($"{resourceType}/{resourceId}", cancellationToken);
        }

        public async Task<FhirResponse<T>> ReadAsync<T>(string uri, CancellationToken cancellationToken = default)
            where T : Resource
        {
            var message = new HttpRequestMessage(HttpMethod.Get, uri);
            message.Headers.Accept.Add(_mediaType);

            HttpResponseMessage response = await HttpClient.SendAsync(message, cancellationToken);

            await EnsureSuccessStatusCodeAsync(response);

            return await CreateResponseAsync<T>(response);
        }

        public Task<FhirResponse<T>> VReadAsync<T>(ResourceType resourceType, string resourceId, string versionId, CancellationToken cancellationToken = default)
            where T : Resource
        {
            return ReadAsync<T>($"{resourceType}/{resourceId}/_history/{versionId}", cancellationToken);
        }

        public Task<FhirResponse<T>> UpdateAsync<T>(T resource, string ifMatchVersion = null, string provenanceHeader = null, CancellationToken cancellationToken = default)
            where T : Resource
        {
            return UpdateAsync($"{resource.ResourceType}/{resource.Id}", resource, ifMatchVersion, provenanceHeader, cancellationToken);
        }

        public Task<FhirResponse<T>> ConditionalUpdateAsync<T>(T resource, string searchCriteria, string ifMatchVersion = null, string provenanceHeader = null, CancellationToken cancellationToken = default)
            where T : Resource
        {
            return UpdateAsync($"{resource.ResourceType}?{searchCriteria}", resource, ifMatchVersion, provenanceHeader, cancellationToken);
        }

        public async Task<FhirResponse<T>> UpdateAsync<T>(string uri, T resource, string ifMatchVersion = null, string provenanceHeader = null, CancellationToken cancellationToken = default)
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

            if (provenanceHeader != null)
            {
                request.Headers.Add(ProvenanceHeader, provenanceHeader);
            }

            HttpResponseMessage response = await HttpClient.SendAsync(request, cancellationToken);

            await EnsureSuccessStatusCodeAsync(response);

            return await CreateResponseAsync<T>(response);
        }

        public Task<FhirResponse> DeleteAsync<T>(T resource, CancellationToken cancellationToken = default)
            where T : Resource
        {
            return DeleteAsync($"{resource.ResourceType}/{resource.Id}", cancellationToken);
        }

        public async Task<FhirResponse> DeleteAsync(string uri, CancellationToken cancellationToken = default)
        {
            var message = new HttpRequestMessage(HttpMethod.Delete, uri);
            message.Headers.Accept.Add(_mediaType);

            HttpResponseMessage response = await HttpClient.SendAsync(message, cancellationToken);

            await EnsureSuccessStatusCodeAsync(response);

            return new FhirResponse(response);
        }

        public Task<FhirResponse> HardDeleteAsync<T>(T resource, CancellationToken cancellationToken = default)
            where T : Resource
        {
            return DeleteAsync($"{resource.ResourceType}/{resource.Id}?hardDelete=true", cancellationToken);
        }

        public async Task<FhirResponse> PatchAsync(string uri, string content, CancellationToken cancellationToken = default)
        {
            var message = new HttpRequestMessage(HttpMethod.Patch, uri)
            {
                Content = new StringContent(content),
            };
            message.Headers.Accept.Add(_mediaType);

            HttpResponseMessage response = await HttpClient.SendAsync(message, cancellationToken);

            await EnsureSuccessStatusCodeAsync(response);

            return new FhirResponse(response);
        }

        public Task<FhirResponse<Bundle>> SearchAsync(ResourceType resourceType, string query = null, int? count = null, CancellationToken cancellationToken = default)
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

            return SearchAsync(sb.ToString(), null, cancellationToken);
        }

        public async Task<FhirResponse<Bundle>> SearchAsync(string url, CancellationToken cancellationToken = default)
        {
            return await SearchAsync(url, null, cancellationToken);
        }

        public async Task<FhirResponse<Bundle>> SearchAsync(string url, Tuple<string, string> customHeader, CancellationToken cancellationToken = default)
        {
            var message = new HttpRequestMessage(HttpMethod.Get, url);
            message.Headers.Accept.Add(_mediaType);

            if (customHeader != null)
            {
                message.Headers.Add(customHeader.Item1, customHeader.Item2);
            }

            HttpResponseMessage response = await HttpClient.SendAsync(message, cancellationToken);

            await EnsureSuccessStatusCodeAsync(response);

            return await CreateResponseAsync<Bundle>(response);
        }

        public async Task<FhirResponse<Bundle>> SearchPostAsync(string resourceType, CancellationToken cancellationToken = default, params (string key, string value)[] body)
        {
            var message = new HttpRequestMessage(HttpMethod.Post, $"{(string.IsNullOrEmpty(resourceType) ? null : $"{resourceType}/")}_search")
            {
                Content = new FormUrlEncodedContent(body.ToDictionary(p => p.key, p => p.value)),
            };
            HttpResponseMessage response = await HttpClient.SendAsync(message, cancellationToken);

            await EnsureSuccessStatusCodeAsync(response);

            return await CreateResponseAsync<Bundle>(response);
        }

        public async Task<Uri> ExportAsync(string path = "", string parameters = "", CancellationToken cancellationToken = default)
        {
            string requestPath = $"{path}$export?{parameters}";
            using var message = new HttpRequestMessage(HttpMethod.Get, requestPath);
            message.Headers.Add("Accept", "application/fhir+json");
            message.Headers.Add("Prefer", "respond-async");

            HttpResponseMessage response = await HttpClient.SendAsync(message, cancellationToken);

            await EnsureSuccessStatusCodeAsync(response);

            return response.Content.Headers.ContentLocation;
        }

        public async Task<Uri> AnonymizedExportAsync(string anonymizationConfig, string container, string etag = null, CancellationToken cancellationToken = default)
        {
            anonymizationConfig = HttpUtility.UrlEncode(anonymizationConfig);
            etag = HttpUtility.UrlEncode(etag);
            container = HttpUtility.UrlEncode(container);
            string requestUrl = $"$export?_container={container}&_anonymizationConfig={anonymizationConfig}&_anonymizationConfigEtag={etag}";

            using var message = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            message.Headers.Add("Accept", "application/fhir+json");
            message.Headers.Add("Prefer", "respond-async");

            HttpResponseMessage response = await HttpClient.SendAsync(message, cancellationToken);

            await EnsureSuccessStatusCodeAsync(response);

            return response.Content.Headers.ContentLocation;
        }

        public async Task<HttpResponseMessage> CheckExportAsync(Uri contentLocation, CancellationToken cancellationToken = default)
        {
            using var message = new HttpRequestMessage(HttpMethod.Get, contentLocation);
            HttpResponseMessage response = await HttpClient.SendAsync(message, cancellationToken);

            return response;
        }

        public async Task CancelExport(Uri contentLocation, CancellationToken cancellationToken = default)
        {
            using var message = new HttpRequestMessage(HttpMethod.Delete, contentLocation);
            await HttpClient.SendAsync(message, cancellationToken);
        }

        public async Task<string> ConvertDataAsync(Parameters parameters, CancellationToken cancellationToken = default)
        {
            string requestPath = "$convert-data";
            var message = new HttpRequestMessage(HttpMethod.Post, requestPath)
            {
                Content = CreateStringContent(parameters),
            };

            HttpResponseMessage response = await HttpClient.SendAsync(message, cancellationToken);

            await EnsureSuccessStatusCodeAsync(response);

            return await response.Content.ReadAsStringAsync();
        }

        public async Task<FhirResponse<Bundle>> PostBundleAsync(Resource bundle, CancellationToken cancellationToken = default)
        {
            var message = new HttpRequestMessage(HttpMethod.Post, string.Empty)
            {
                Content = CreateStringContent(bundle),
            };

            message.Headers.Accept.Add(_mediaType);

            HttpResponseMessage response = await HttpClient.SendAsync(message, cancellationToken);

            await EnsureSuccessStatusCodeAsync(response);

            return await CreateResponseAsync<Bundle>(response);
        }

        public async Task<(FhirResponse<Parameters>, Uri)> PostReindexJobAsync(
            Parameters parameters,
            string uniqueResource = null,
            CancellationToken cancellationToken = default)
        {
            var message = new HttpRequestMessage(HttpMethod.Post, $"{uniqueResource}$reindex")
            {
                Content = CreateStringContent(parameters),
            };

            HttpResponseMessage response = await HttpClient.SendAsync(message, cancellationToken);

            await EnsureSuccessStatusCodeAsync(response);

            return (await CreateResponseAsync<Parameters>(response), response.Content.Headers.ContentLocation);
        }

        public async Task<FhirResponse<Parameters>> CheckReindexAsync(Uri contentLocation, CancellationToken cancellationToken = default)
        {
            using var message = new HttpRequestMessage(HttpMethod.Get, contentLocation);
            HttpResponseMessage response = await HttpClient.SendAsync(message, cancellationToken);

            return await CreateResponseAsync<Parameters>(response);
        }

        /// <summary>
        /// Calls the $validate endpoint.
        /// </summary>
        /// <param name="uri">The URL to call</param>
        /// <param name="resource">The resource to be validated. The resource parameter is a string instead of a Resource object because the validate endpoint is frequently sent invalid resources that couldn't be parsed.</param>
        /// <param name="xml">Whether the resource is in JSON or XML formal</param>
        /// <param name="cancellationToken">The cancellation token</param>
        public async Task<OperationOutcome> ValidateAsync(string uri, string resource, bool xml = false, CancellationToken cancellationToken = default)
        {
            var message = new HttpRequestMessage(HttpMethod.Post, xml ? uri + "?_format=xml" : uri);
            message.Content = new StringContent(resource, Encoding.UTF8, xml ? ContentType.XML_CONTENT_HEADER : ContentType.JSON_CONTENT_HEADER);

            HttpResponseMessage response = await HttpClient.SendAsync(message, cancellationToken);

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
    }
}
