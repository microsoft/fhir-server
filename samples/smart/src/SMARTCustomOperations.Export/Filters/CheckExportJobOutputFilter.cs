// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using System.Security;
using System.Security.Policy;
using Microsoft.AzureHealth.DataServices.Filters;
using Microsoft.AzureHealth.DataServices.Pipelines;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using SMARTCustomOperations.AzureAuth.Extensions;
using SMARTCustomOperations.Export.Configuration;

namespace SMARTCustomOperations.Export.Filters
{
    public class CheckExportJobOutputFilter : IOutputFilter
    {
        private readonly ILogger _logger;
        private readonly ExportCustomOperationsConfig _configuration;
        private readonly string _id;

        public CheckExportJobOutputFilter(ILogger<CheckExportJobOutputFilter> logger, ExportCustomOperationsConfig configuration)
        {
            _logger = logger;
            _configuration = configuration;
            _id = Guid.NewGuid().ToString();
        }

        public event EventHandler<FilterErrorEventArgs>? OnFilterError;

        public string Name => nameof(CheckExportJobOutputFilter);

        public StatusType ExecutionStatusType => StatusType.Normal;

        public string Id => _id;

        public Task<OperationContext> ExecuteAsync(OperationContext context)
        {
            // Only execute filter fof export job check operations
            if (context.Properties["PipelineType"] != ExportOperationType.ExportCheck.ToString())
            {
                return Task.FromResult(context);
            }

            // Don't process running or failed export operations
            if (context.StatusCode != HttpStatusCode.OK)
            {
                return Task.FromResult(context);
            }

            _logger?.LogInformation("Entered {Name}", Name);

            Uri requestUri = context.Request!.RequestUri!;

            try
            {
                // Replace the content location URL with the public endpoint
                var jBody = JObject.Parse(context.ContentString);

                jBody["requiresAccessToken"] = true;

                foreach (JToken output in (JArray)jBody.SelectToken("output")!)
                {
                    // Get the orig URL of the output array item
                    var outputObj = (JObject)output;
                    var origUrl = new Uri(outputObj["url"]!.ToString());

                    // #TODO - test this logic
                    if (!origUrl.LocalPath.StartsWith("/" + context.Properties["oid"], StringComparison.InvariantCulture))
                    {
                        var ex = new SecurityException($"User attempted export with token with wrong oid claim. {Id}. OID: {context.Properties["oid"]}. Container: {origUrl.Segments[1]}.");

                        FilterErrorEventArgs error = new(name: Name, id: Id, fatal: true, error: ex, code: HttpStatusCode.Unauthorized);
                        OnFilterError?.Invoke(this, error);
                        return Task.FromResult(context.SetContextErrorBody(error, _configuration.Debug));
                    }

                    outputObj["url"] = BuildNewExportFileUri(_configuration.ApiManagementHostName!, _configuration.ApiManagementFhirPrefex, origUrl.LocalPath);
                }

                context.ContentString = jBody.ToString();
            }
            catch (Exception ex)
            {
#pragma warning disable CA2201
                FilterErrorEventArgs error = new(name: Name, id: Id, fatal: true, error: new Exception($"Could not process export check result.", ex), code: HttpStatusCode.InternalServerError, responseBody: context.ContentString);
                OnFilterError?.Invoke(this, error);
                return Task.FromResult(context.SetContextErrorBody(error, _configuration.Debug));
            }

            return Task.FromResult(context);
        }

        // Maps the url segment to our proxied storage endpoint
        private static Uri BuildNewExportFileUri(string apiManagementHostname, string? apiManagementEndpointPrefix, string requestLocalPath)
        {
            var outputUriBuilder = new UriBuilder();
            outputUriBuilder.Scheme = "https://";
            outputUriBuilder.Host = apiManagementHostname;

            // Add the API prefix if it exists
            if (apiManagementEndpointPrefix is not null)
            {
                outputUriBuilder.Path += $"{apiManagementEndpointPrefix}/";
            }

            outputUriBuilder.Path += "_export";
            outputUriBuilder.Path += requestLocalPath;

            return outputUriBuilder.Uri;
        }
    }
}
