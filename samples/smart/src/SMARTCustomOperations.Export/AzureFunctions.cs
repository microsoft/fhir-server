// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.AzureHealth.DataServices.Pipelines;
using Microsoft.Extensions.Logging;

namespace SMARTCustomOperations.Export
{
    public class AzureFunctions
    {
        private readonly ILogger _logger;
        private readonly IPipeline<HttpRequestData, HttpResponseData> _pipeline;

        public AzureFunctions(ILogger<AzureFunctions> logger, IPipeline<HttpRequestData, HttpResponseData> pipeline)
        {
            _logger = logger;
            _pipeline = pipeline;
        }

        [Function("StartGroupExport")]
        public async Task<HttpResponseData> RunGroupExportFunction([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "Group/{logicalId}/$export")] HttpRequestData req)
        {
            _logger.LogInformation("StartGroupExport function pipeline started.");
            var result = await _pipeline.ExecuteAsync(req);

            // Toolkit uses content-length instead of transfer encoding
            if (result.Headers.Contains("Transfer-Encoding"))
            {
                result.Headers.Remove("Transfer-Encoding");
            }

            // Toolkit does not support content headers - workaround.
            if (result.Headers.Any(x => x.Key == "Custom-Content-Locaton"))
            {
                result.Headers.Add("Content-Location", result.Headers.First(x => x.Key == "Custom-Content-Locaton").Value);
                result.Headers.Remove("Custom-Content-Locaton");
            }

            return result;
        }

        [Function("ExportJob")]
        public async Task<HttpResponseData> RunExportJobFunction([HttpTrigger(AuthorizationLevel.Anonymous, "get", "delete", Route = "_operations/export/{id}")] HttpRequestData req)
        {
            _logger.LogInformation("ExportJob function pipeline started.");
            var result = await _pipeline.ExecuteAsync(req);

            // Toolkit uses content-length instead of transfer encoding
            if (result.Headers.Contains("Transfer-Encoding"))
            {
                result.Headers.Remove("Transfer-Encoding");
            }

            return result;
        }
    }
}
