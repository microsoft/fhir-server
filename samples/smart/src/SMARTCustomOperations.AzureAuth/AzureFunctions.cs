// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.AzureHealth.DataServices.Pipelines;
using Microsoft.Extensions.Logging;

namespace SMARTCustomOperations.AzureAuth
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

        [Function("Authorize")]
        public async Task<HttpResponseData> RunAuthorizeFunction([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "authorize")] HttpRequestData req)
        {
            _logger.LogInformation("AuthorizeInputFilter function pipeline started.");
            var result = await _pipeline.ExecuteAsync(req);
            return result;
        }

        [Function("Token")]
        public async Task<HttpResponseData> RunTokenFunction([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "token")] HttpRequestData req)
        {
            _logger.LogInformation("Token function pipeline started.");

            return await _pipeline.ExecuteAsync(req);
        }
    }
}
