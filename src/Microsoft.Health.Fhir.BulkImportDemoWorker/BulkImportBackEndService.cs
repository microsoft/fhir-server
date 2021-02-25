// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.BulkImportDemoWorker
{
    public class BulkImportBackEndService : BackgroundService
    {
        internal static readonly Encoding ResourceEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

        private ISearchIndexer _searchIndexer;
        private IRawResourceFactory _rawResourceFactory;
        private ResourceIdProvider _resourceIdProvider;
        private IConfiguration _configuration;

        public BulkImportBackEndService(
            ISearchIndexer searchIndexer,
            IRawResourceFactory rawResourceFactory,
            ResourceIdProvider resourceIdProvider,
            IConfiguration configuration)
        {
            _searchIndexer = searchIndexer;
            _rawResourceFactory = rawResourceFactory;
            _resourceIdProvider = resourceIdProvider;
            _configuration = configuration;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Channel<string> rawDataChannel = Channel.CreateBounded<string>(30000);
            Channel<BulkCopyResourceWrapper> processedResourceChannel = Channel.CreateBounded<BulkCopyResourceWrapper>(3000);
            Channel<BulkCopySearchParamWrapper> searchParamChannel = Channel.CreateBounded<BulkCopySearchParamWrapper>(30000);

            ModelProvider provider = ModelProvider.CreateModelProvider();
            string clientId = _configuration["ClientId"];
            string tenantId = _configuration["TenantId"];
            string secret = _configuration["Secret"];
            LoadRawDataStep loadRawDataStep = new LoadRawDataStep(rawDataChannel, new Uri("https://adfasia.blob.core.windows.net/synthea/test1/Patient.ndjson"), GetClientSecretCredential(tenantId, clientId, secret));
            ProcessFhirResourceStep processFhirResourceStep = new ProcessFhirResourceStep(rawDataChannel, processedResourceChannel, searchParamChannel, _searchIndexer, _rawResourceFactory);
            ImportResourceTableStep importResourceTableStep = new ImportResourceTableStep(processedResourceChannel, _resourceIdProvider, provider, _configuration);
            ImportSearchParamStep importToStringSearchParamTableStep = new ImportSearchParamStep(searchParamChannel, provider, _configuration);

            processFhirResourceStep.Start();
            importResourceTableStep.Start();
            loadRawDataStep.Start();
            importToStringSearchParamTableStep.Start();

            await loadRawDataStep.WaitForStopAsync();
            await processFhirResourceStep.WaitForStopAsync();
            await importResourceTableStep.WaitForStopAsync();
            await importToStringSearchParamTableStep.WaitForStopAsync();
        }

        private static ClientSecretCredential GetClientSecretCredential(string tenantId, string clientId, string clientSecret)
        {
            return new ClientSecretCredential(
                tenantId, clientId, clientSecret, new TokenCredentialOptions());
        }
    }
}
