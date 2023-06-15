// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Diagnostics;
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
        private const string BundleProcessingLogicHeader = "x-bundle-processing-logic";
        private const string IfNoneExistHeaderName = "If-None-Exist";
        private const string ProvenanceHeader = "X-Provenance";
        private const string IfMatchHeaderName = "If-Match";
        public const string ProfileValidation = "x-ms-profile-validation";
        public const string ReindexParametersStatus = "Status";

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
            return CreateAsync(resource.TypeName, resource, conditionalCreateCriteria, provenanceHeader, cancellationToken);
        }

        public async Task<FhirResponse<T>> CreateAsync<T>(string uri, T resource, string conditionalCreateCriteria = null, string provenanceHeader = null, CancellationToken cancellationToken = default)
            where T : Resource
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, uri);
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
            using var message = new HttpRequestMessage(HttpMethod.Get, uri);
            message.Headers.Accept.Add(_mediaType);

            using HttpResponseMessage response = await HttpClient.SendAsync(message, cancellationToken);

            await EnsureSuccessStatusCodeAsync(response);

            return await CreateResponseAsync<T>(response);
        }

        public Task<FhirResponse<T>> VReadAsync<T>(ResourceType resourceType, string resourceId, string versionId, CancellationToken cancellationToken = default)
            where T : Resource
        {
            return ReadAsync<T>($"{resourceType}/{resourceId}/_history/{versionId}", cancellationToken);
        }

        public Task<FhirResponse<T>> UpdateAsync<T>(T resource, string ifMatchHeaderETag = null, string provenanceHeader = null, CancellationToken cancellationToken = default)
            where T : Resource
        {
            return UpdateAsync($"{resource.TypeName}/{resource.Id}", resource, ifMatchHeaderETag, provenanceHeader, cancellationToken);
        }

        public Task<FhirResponse<T>> ConditionalUpdateAsync<T>(T resource, string searchCriteria, string ifMatchHeaderETag = null, string provenanceHeader = null, CancellationToken cancellationToken = default)
            where T : Resource
        {
            return UpdateAsync($"{resource.TypeName}?{searchCriteria}", resource, ifMatchHeaderETag, provenanceHeader, cancellationToken);
        }

        public async Task<FhirResponse<T>> UpdateAsync<T>(string uri, T resource, string ifMatchHeaderETag = null, string provenanceHeader = null, CancellationToken cancellationToken = default)
            where T : Resource
        {
            using var message = new HttpRequestMessage(HttpMethod.Put, uri)
            {
                Content = CreateStringContent(resource),
            };
            message.Headers.Accept.Add(_mediaType);

            if (ifMatchHeaderETag != null)
            {
                message.Headers.Add(IfMatchHeaderName, ifMatchHeaderETag);
            }

            if (provenanceHeader != null)
            {
                message.Headers.Add(ProvenanceHeader, provenanceHeader);
            }

            using HttpResponseMessage response = await HttpClient.SendAsync(message, cancellationToken);

            await EnsureSuccessStatusCodeAsync(response);

            return await CreateResponseAsync<T>(response);
        }

        public Task<FhirResponse> DeleteAsync<T>(T resource, CancellationToken cancellationToken = default)
            where T : Resource
        {
            return DeleteAsync($"{resource.TypeName}/{resource.Id}", cancellationToken);
        }

        public async Task<FhirResponse> DeleteAsync(string uri, CancellationToken cancellationToken = default)
        {
            using var message = new HttpRequestMessage(HttpMethod.Delete, uri);
            message.Headers.Accept.Add(_mediaType);

            using HttpResponseMessage response = await HttpClient.SendAsync(message, cancellationToken);

            await EnsureSuccessStatusCodeAsync(response);

            return new FhirResponse(response);
        }

        public Task<FhirResponse> HardDeleteAsync<T>(T resource, CancellationToken cancellationToken = default)
            where T : Resource
        {
            return DeleteAsync($"{resource.TypeName}/{resource.Id}?hardDelete=true", cancellationToken);
        }

        public async Task<FhirResponse<T>> JsonPatchAsync<T>(T resource, string content, string ifMatchVersion = null, CancellationToken cancellationToken = default)
            where T : Resource
        {
            return await JsonPatchAsync<T>($"{resource.TypeName}/{resource.Id}", content, ifMatchVersion, cancellationToken);
        }

        public async Task<FhirResponse<T>> ConditionalJsonPatchAsync<T>(string resourceType, string searchCriteria, string content, string ifMatchVersion = null, CancellationToken cancellationToken = default)
                 where T : Resource
        {
            return await JsonPatchAsync<T>($"{resourceType}?{searchCriteria}", content, ifMatchVersion, cancellationToken);
        }

        private async Task<FhirResponse<T>> JsonPatchAsync<T>(string uri, string content, string ifMatchVersion = null, CancellationToken cancellationToken = default)
           where T : Resource
        {
            using var message = new HttpRequestMessage(HttpMethod.Patch, uri)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json-patch+json"),
            };

            message.Headers.Accept.Add(_mediaType);

            if (ifMatchVersion != null)
            {
                var weakETag = $"W/\"{ifMatchVersion}\"";

                message.Headers.Add(IfMatchHeaderName, weakETag);
            }

            using HttpResponseMessage response = await HttpClient.SendAsync(message, cancellationToken);

            await EnsureSuccessStatusCodeAsync(response);

            return await CreateResponseAsync<T>(response);
        }

        public async Task<FhirResponse<T>> FhirPatchAsync<T>(T resource, Parameters patchRequest, string ifMatchVersion = null, CancellationToken cancellationToken = default)
            where T : Resource
        {
            return await FhirPatchAsync<T>($"{resource.TypeName}/{resource.Id}", patchRequest, ifMatchVersion, cancellationToken);
        }

        public async Task<FhirResponse<T>> ConditionalFhirPatchAsync<T>(string resourceType, string searchCriteria, Parameters patchRequest, string ifMatchVersion = null, CancellationToken cancellationToken = default)
                 where T : Resource
        {
            return await FhirPatchAsync<T>($"{resourceType}?{searchCriteria}", patchRequest, ifMatchVersion, cancellationToken);
        }

        private async Task<FhirResponse<T>> FhirPatchAsync<T>(string uri, Parameters patchRequest, string ifMatchVersion = null, CancellationToken cancellationToken = default)
           where T : Resource
        {
            using var message = new HttpRequestMessage(HttpMethod.Patch, uri)
            {
                Content = CreateStringContent(patchRequest),
            };

            message.Headers.Accept.Add(_mediaType);

            if (ifMatchVersion != null)
            {
                var weakETag = $"W/\"{ifMatchVersion}\"";

                message.Headers.Add(IfMatchHeaderName, weakETag);
            }

            using HttpResponseMessage response = await HttpClient.SendAsync(message, cancellationToken);

            await EnsureSuccessStatusCodeAsync(response);

            return await CreateResponseAsync<T>(response);
        }

        public Task<FhirResponse<Bundle>> SearchAsync(ResourceType resourceType, string query = null, int? count = null, CancellationToken cancellationToken = default)
        {
            var sb = new StringBuilder();

            sb.Append(resourceType).Append('?');

            if (query != null)
            {
                sb.Append(query);
            }

            if (count != null)
            {
                if (sb[^1] != '?')
                {
                    sb.Append('&');
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
            using var message = new HttpRequestMessage(HttpMethod.Get, url);
            message.Headers.Accept.Add(_mediaType);

            if (customHeader != null)
            {
                message.Headers.Add(customHeader.Item1, customHeader.Item2);
            }

            using HttpResponseMessage response = await HttpClient.SendAsync(message, cancellationToken);

            await EnsureSuccessStatusCodeAsync(response);

            return await CreateResponseAsync<Bundle>(response);
        }

        public async Task<FhirResponse<Bundle>> SearchPostAsync(string resourceType, string query, CancellationToken cancellationToken = default, params (string key, string value)[] body)
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, $"{(string.IsNullOrEmpty(resourceType) ? null : $"{resourceType}/")}_search?{query}")
            {
                Content = new FormUrlEncodedContent(body.ToDictionary(p => p.key, p => p.value)),
            };

            using HttpResponseMessage response = await HttpClient.SendAsync(message, cancellationToken);

            await EnsureSuccessStatusCodeAsync(response);

            return await CreateResponseAsync<Bundle>(response);
        }

        public async Task<FhirResponse<Resource>> PostAsync(string resourceType, string body, CancellationToken cancellationToken = default)
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, $"{(string.IsNullOrEmpty(resourceType) ? null : $"{resourceType}/")}")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            message.Headers.Accept.Add(_mediaType);
            HttpResponseMessage response = await HttpClient.SendAsync(message, cancellationToken);

            await EnsureSuccessStatusCodeAsync(response);

            return await CreateResponseAsync<Resource>(response);
        }

        public async Task<Uri> ExportAsync(string path = "", string parameters = "", CancellationToken cancellationToken = default)
        {
            string requestPath = $"{path}$export?{parameters}";
            using var message = new HttpRequestMessage(HttpMethod.Get, requestPath);
            message.Headers.Add("Accept", "application/fhir+json");
            message.Headers.Add("Prefer", "respond-async");

            using HttpResponseMessage response = await HttpClient.SendAsync(message, cancellationToken);

            await EnsureSuccessStatusCodeAsync(response);

            return response.Content.Headers.ContentLocation;
        }

        public async Task<Uri> AnonymizedExportAsync(string anonymizationConfig, DateTimeOffset since, string container, string etag = null, string path = "", CancellationToken cancellationToken = default)
        {
            anonymizationConfig = HttpUtility.UrlEncode(anonymizationConfig);
            etag = HttpUtility.UrlEncode(etag);
            container = HttpUtility.UrlEncode(container);
            string requestUrl = $"{path}$export?_since={since.ToString("yyyy-MM-ddTHH:mm:ss")}&_container={container}&_anonymizationConfig={anonymizationConfig}&_anonymizationConfigEtag={etag}";

            using var message = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            message.Headers.Add("Accept", "application/fhir+json");
            message.Headers.Add("Prefer", "respond-async");

            using HttpResponseMessage response = await HttpClient.SendAsync(message, cancellationToken);

            await EnsureSuccessStatusCodeAsync(response);

            return response.Content.Headers.ContentLocation;
        }

        public async Task<Uri> AnonymizedExportUsingAcrAsync(string anonymizationConfig, string anonymizationConfigCollectionReference, DateTimeOffset since, string container, string path = "", CancellationToken cancellationToken = default)
        {
            anonymizationConfig = HttpUtility.UrlEncode(anonymizationConfig);
            anonymizationConfigCollectionReference = HttpUtility.UrlEncode(anonymizationConfigCollectionReference);
            container = HttpUtility.UrlEncode(container);
            string requestUrl = $"{path}$export?_since={since.ToString("yyyy-MM-ddTHH:mm:ss")}&_container={container}&_anonymizationConfig={anonymizationConfig}&_anonymizationConfigCollectionReference={anonymizationConfigCollectionReference}";

            using var message = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            message.Headers.Add("Accept", "application/fhir+json");
            message.Headers.Add("Prefer", "respond-async");

            using HttpResponseMessage response = await HttpClient.SendAsync(message, cancellationToken);

            await EnsureSuccessStatusCodeAsync(response);

            return response.Content.Headers.ContentLocation;
        }

        public async Task<HttpResponseMessage> CheckExportAsync(Uri contentLocation, CancellationToken cancellationToken = default)
        {
            using var message = new HttpRequestMessage(HttpMethod.Get, contentLocation);
            var response = await HttpClient.SendAsync(message, cancellationToken);

            return response;
        }

        public async Task CancelExport(Uri contentLocation, CancellationToken cancellationToken = default)
        {
            using var message = new HttpRequestMessage(HttpMethod.Delete, contentLocation);
            await HttpClient.SendAsync(message, cancellationToken);
        }

        public async Task<FhirResponse> ConvertDataAsync(Parameters parameters, CancellationToken cancellationToken = default)
        {
            string requestPath = "$convert-data";
            using var message = new HttpRequestMessage(HttpMethod.Post, requestPath)
            {
                Content = CreateStringContent(parameters),
            };

            using HttpResponseMessage response = await HttpClient.SendAsync(message, cancellationToken);

            await EnsureSuccessStatusCodeAsync(response);

            return new FhirResponse(response);
        }

        public async Task<Uri> ImportAsync(Parameters parameters, CancellationToken cancellationToken = default)
        {
            string requestPath = "$import";
            using var message = new HttpRequestMessage(HttpMethod.Post, requestPath)
            {
                Content = CreateStringContent(parameters),
            };

            message.Headers.Add("Prefer", "respond-async");

            using HttpResponseMessage response = await HttpClient.SendAsync(message, cancellationToken);

            await EnsureSuccessStatusCodeAsync(response);

            return response.Content.Headers.ContentLocation;
        }

        public async Task<HttpResponseMessage> CancelImport(Uri contentLocation, CancellationToken cancellationToken = default)
        {
            using var message = new HttpRequestMessage(HttpMethod.Delete, contentLocation);
            message.Headers.Add("Prefer", "respond-async");

            return await HttpClient.SendAsync(message, cancellationToken);
        }

        public async Task<HttpResponseMessage> CheckImportAsync(Uri contentLocation, CancellationToken cancellationToken = default)
        {
            using var message = new HttpRequestMessage(HttpMethod.Get, contentLocation);
            message.Headers.Add("Prefer", "respond-async");

            var response = await HttpClient.SendAsync(message, cancellationToken);

            await EnsureSuccessStatusCodeAsync(response);

            return response;
        }

        public async Task<FhirResponse<Bundle>> PostBundleAsync(Resource bundle, FhirBundleProcessingLogic processingLogic = FhirBundleProcessingLogic.Parallel, CancellationToken cancellationToken = default)
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, string.Empty)
            {
                Content = CreateStringContent(bundle),
            };

            message.Headers.Accept.Add(_mediaType);
            message.Headers.Add(BundleProcessingLogicHeader, processingLogic.ToString());

            using HttpResponseMessage response = await HttpClient.SendAsync(message, cancellationToken);

            await EnsureSuccessStatusCodeAsync(response);

            return await CreateResponseAsync<Bundle>(response);
        }

        public async Task<FhirResponse<Bundle>> PostBundleWithValidationHeaderAsync(Resource bundle, bool profileValidation, FhirBundleProcessingLogic processingLogic = FhirBundleProcessingLogic.Parallel, CancellationToken cancellationToken = default)
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, string.Empty)
            {
                Content = CreateStringContent(bundle),
            };
            message.Headers.Add(ProfileValidation, profileValidation.ToString());
            message.Headers.Accept.Add(_mediaType);
            message.Headers.Add(BundleProcessingLogicHeader, processingLogic.ToString());

            using HttpResponseMessage response = await HttpClient.SendAsync(message, cancellationToken);

            await EnsureSuccessStatusCodeAsync(response);

            return await CreateResponseAsync<Bundle>(response);
        }

        public async Task<(FhirResponse<Parameters> reponse, Uri uri)> PostReindexJobAsync(
            Parameters parameters,
            string uniqueResource = null,
            CancellationToken cancellationToken = default)
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, $"{uniqueResource}$reindex")
            {
                Content = CreateStringContent(parameters),
            };

            using HttpResponseMessage response = await HttpClient.SendAsync(message, cancellationToken);

            await EnsureSuccessStatusCodeAsync(response);

            return (await CreateResponseAsync<Parameters>(response), response.Content.Headers.ContentLocation);
        }

        public async Task<FhirResponse<Parameters>> CheckReindexAsync(Uri contentLocation, CancellationToken cancellationToken = default)
        {
            using var message = new HttpRequestMessage(HttpMethod.Get, contentLocation);
            using HttpResponseMessage response = await HttpClient.SendAsync(message, cancellationToken);

            return await CreateResponseAsync<Parameters>(response);
        }

        public async Task<FhirResponse<Parameters>> WaitForReindexStatus(Uri reindexJobUri, params string[] desiredStatus)
        {
            int checkReindexCount = 0;
            int maxCount = 30;
            var delay = TimeSpan.FromSeconds(10);
            var sw = new Stopwatch();
            string currentStatus;
            FhirResponse<Parameters> reindexJobResult;
            sw.Start();

            do
            {
                if (checkReindexCount > 0)
                {
                    await Task.Delay(delay);
                }

                reindexJobResult = await CheckReindexAsync(reindexJobUri);
                currentStatus = reindexJobResult.Resource.Parameter.FirstOrDefault(p => p.Name == ReindexParametersStatus)?.Value.ToString();
                checkReindexCount++;
            }
            while (!desiredStatus.Contains(currentStatus) && checkReindexCount < maxCount);

            sw.Stop();

            if (checkReindexCount >= maxCount)
            {
#pragma warning disable CA2201 // Do not raise reserved exception types. This is used in a test and has a specific message.
                throw new Exception($"ReindexJob did not complete within {checkReindexCount} attempts and a duration of {sw.Elapsed.Duration()}. This may cause other tests using Reindex to fail.");
#pragma warning restore CA2201 // Do not raise reserved exception types
            }

            return reindexJobResult;
        }

        /// <summary>
        /// Calls the $validate endpoint.
        /// </summary>
        /// <param name="uri">The URL to call</param>
        /// <param name="resource">The resource to be validated. The resource parameter is a string instead of a Resource object because the validate endpoint is frequently sent invalid resources that couldn't be parsed.</param>
        /// <param name="profile">Profile uri to check resource against.</param>
        /// <param name="cancellationToken">The cancellation token</param>
        public async Task<OperationOutcome> ValidateAsync(string uri, string resource, string profile = null, CancellationToken cancellationToken = default)
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, profile != null ? uri + $"?profile={profile}" : uri)
            {
                Content = new StringContent(resource, Encoding.UTF8, ContentType.JSON_CONTENT_HEADER),
            };

            using HttpResponseMessage response = await HttpClient.SendAsync(message, cancellationToken);

            await EnsureSuccessStatusCodeAsync(response);

            return await CreateResponseAsync<OperationOutcome>(response);
        }

        public async Task<OperationOutcome> ValidateByIdAsync(ResourceType resourceType, string resourceId, string profile, CancellationToken cancellationToken = default)
        {
            var uri = $"{resourceType}/{resourceId}/$validate";
            using var message = new HttpRequestMessage(HttpMethod.Get, profile != null ? uri + $"?profile={profile}" : uri);

            using HttpResponseMessage response = await HttpClient.SendAsync(message, cancellationToken);

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

                using var message = new HttpRequestMessage(HttpMethod.Get, "health/check");
                using HttpResponseMessage healthCheck = await HttpClient.SendAsync(message, CancellationToken.None);

                FhirResponse<OperationOutcome> operationOutcome;
                try
                {
                    operationOutcome = await CreateResponseAsync<OperationOutcome>(response);
                }
                catch (Exception)
                {
                    // The response could not be read as an OperationOutcome. Throw a generic HTTP error.
                    throw new HttpRequestException($"Status code: {response.StatusCode}; reason phrase: '{response.ReasonPhrase}'; body: '{await response.Content.ReadAsStringAsync()}'; health check: '{healthCheck.StatusCode}'");
                }

                throw new FhirClientException(operationOutcome, healthCheck.StatusCode);
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

        public async Task<Parameters> MemberMatch(Patient patient, Coverage coverage, CancellationToken cancellationToken = default)
        {
            var inParams = new Parameters();
            inParams.Add("MemberPatient", patient);
            inParams.Add("OldCoverage", coverage);

            using var message = new HttpRequestMessage(HttpMethod.Post, "Patient/$member-match");
            message.Headers.Accept.Add(_mediaType);
            message.Content = CreateStringContent(inParams);

            using HttpResponseMessage response = await HttpClient.SendAsync(message, cancellationToken);

            await EnsureSuccessStatusCodeAsync(response);

            return await CreateResponseAsync<Parameters>(response);
        }
    }
}
