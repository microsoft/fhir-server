// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

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
            return result;
        }

        [Function("ExportJob")]
        public async Task<HttpResponseData> RunExportJobFunction([HttpTrigger(AuthorizationLevel.Anonymous, "get", "delete", Route = "_operations/export/{id}")] HttpRequestData req)
        {
            _logger.LogInformation("ExportJob function pipeline started.");
            var result = await _pipeline.ExecuteAsync(req);
            return result;
        }
    }
}
