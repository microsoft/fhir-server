// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Specialized;
using Microsoft.AzureHealth.DataServices.Bindings;
using Microsoft.AzureHealth.DataServices.Clients;
using Microsoft.AzureHealth.DataServices.Clients.Headers;
using Microsoft.AzureHealth.DataServices.Pipelines;
using Microsoft.AzureHealth.DataServices.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SMARTCustomOperations.Export.Configuration;

namespace SMARTCustomOperations.Export.Bindings
{
    public class ExportBinding : IBinding
    {
        private readonly ILogger _logger;
        private readonly IOptions<ExportBindingOptions> _options;
        private readonly IAuthenticator _authenticator;

        public ExportBinding(ILogger<ExportBinding> logger, IOptions<ExportBindingOptions> options, IAuthenticator authenticator)
        {
            _logger = logger;
            Id = Guid.NewGuid().ToString();
            _options = options;
            _authenticator = authenticator;
        }

        /// <summary>
        /// An event that signals an error in the binding.
        /// </summary>
        public event EventHandler<BindingErrorEventArgs>? OnError;

        /// <summary>
        /// An event that signals the binding has completed.
        /// </summary>
        public event EventHandler<BindingCompleteEventArgs>? OnComplete;

        /// <summary>
        /// Gets the name of the binding "AzureActiveDirectoryBinding".
        /// </summary>
        public string Name => nameof(ExportBinding);

        /// <summary>
        /// Gets a unique ID of the binding instance.
        /// </summary>
        public string Id { get; internal set; }

        /// <summary>
        /// Executes the binding.
        /// </summary>
        /// <param name="context">Operation context.</param>
        /// <returns>Operation <paramref name="context"/>.</returns>
        public async Task<OperationContext> ExecuteAsync(OperationContext context)
        {
            _logger?.LogInformation("{Name}-{Id} binding received.", Name, Id);

            // Set from the type of pipeline
            ExportOperationType operationType;
            if (!Enum.TryParse(context.Properties["PipelineType"], out operationType))
            {
                // default error event in WebPipeline.cs of toolkit
                OnError?.Invoke(this, new BindingErrorEventArgs(Id, Name, new ArgumentException("PipelineType", $"Invalid pipeline type encountered in Export Binding {context.Properties["PipelineType"]}")));
                return context;
            }

            RestRequestBuilder builder = await GetBuilderForOperationType(
                operationType,
                context.Request.RequestUri!,
                context.Properties["oid"],
                context.Properties["token"]);

            _logger.LogTrace("Sending export category request to {ServerUrl}{LocalPath}{Query}", builder.BaseUrl, builder.Path, builder.QueryString);

            HttpResponseMessage httpResponseMessage = await new RestRequest(builder).SendAsync();

            var responseContent = await httpResponseMessage.Content.ReadAsStringAsync();

            context.StatusCode = httpResponseMessage.StatusCode;
            context.ContentString = responseContent;

            // Copy all non-restricted headers to context.
            var responseHeaders = httpResponseMessage.GetHeaders();
            foreach (var headerName in responseHeaders.AllKeys)
            {
                context.Headers.Add(new HeaderNameValuePair(headerName, responseHeaders[headerName], CustomHeaderType.ResponseStatic));
            }

            if (httpResponseMessage.Content.Headers.ContentLocation is not null)
            {
                context.Headers.Add(new HeaderNameValuePair("Content-Location", httpResponseMessage.Content.Headers.ContentLocation.OriginalString, CustomHeaderType.ResponseStatic));
            }

            _logger?.LogTrace("Backend {ServerUrl} responded with code {StatusCode} and Body {Body}.", builder.BaseUrl, context.StatusCode, context.ContentString);

            if ((int)context.StatusCode >= 400)
            {
                context.IsFatal = true;
                _logger.LogWarning("Invalid response from ExportBinding. {Code}, {Url}, {Body}, {Headers}", context.StatusCode, $"{builder.BaseUrl}{builder.Path}?{builder.QueryString}", context.ContentString, builder.Headers.ToString());
            }

            OnComplete?.Invoke(this, new BindingCompleteEventArgs(Id, Name, context)); // default complete event in WebPipeline.cs of toolkit
            return context;
        }

        public async Task<RestRequestBuilder> GetBuilderForOperationType(ExportOperationType operationType, Uri requestUri, string oid, string requestToken)
        {
            // Set our default values for the below switch to override
            string localPath = requestUri.LocalPath;
            NameValueCollection queryCollection = requestUri.ParseQueryString();
            NameValueCollection headers = new();
            headers.Add("Prefer", "respond-async");
            string method = "GET";
            string token = requestToken;
            string serverUrl = _options.Value.FhirServerEndpoint!;
            string contentType = "application/json";

            localPath = localPath.Replace($"/api", string.Empty, StringComparison.OrdinalIgnoreCase);
            if (_options.Value.ApiManagementFhirPrefex is not null)
            {
                localPath = localPath.Replace($"/{_options.Value.ApiManagementFhirPrefex}", string.Empty, StringComparison.OrdinalIgnoreCase);
            }

            switch (operationType)
            {
                case ExportOperationType.GroupExport:
                    queryCollection.Remove("_container");
                    queryCollection.Add("_container", oid);
                    contentType = "application/fhir+json";
                    break;
                case ExportOperationType.GetExportFile:
                    token = await _authenticator.AcquireTokenForClientAsync(_options.Value.StorageEndpoint);
                    serverUrl = _options.Value.StorageEndpoint!;
                    localPath = localPath.Replace("/_export", string.Empty, StringComparison.OrdinalIgnoreCase);
                    contentType = "application/fhir+ndjson";
                    break;
                case ExportOperationType.ExportCheck:
                    break;
                default:
                    throw new ArgumentException("Invalid operation type for this pipeline.");
            }

            return new RestRequestBuilder(
                method: method,
                baseUrl: serverUrl,
                securityToken: token,
                path: localPath,
                query: queryCollection.ToString(),
                headers: headers,
                content: null,
                contentType: contentType);
        }
    }
}
