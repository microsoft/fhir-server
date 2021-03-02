// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.BulkImportDemoWorker
{
    public class BulkImportTask : ITask
    {
        public const int BulkImportTaskType = 100;

        private ISearchIndexer _searchIndexer;
        private IRawResourceFactory _rawResourceFactory;
        private ResourceIdProvider _resourceIdProvider;
        private IConfiguration _configuration;
        private BulkImportTaskInput _bulkImportTaskInput;

        public BulkImportTask(
            ISearchIndexer searchIndexer,
            IRawResourceFactory rawResourceFactory,
            ResourceIdProvider resourceIdProvider,
            IConfiguration configuration,
            BulkImportTaskInput bulkImportTaskInput)
        {
            _searchIndexer = searchIndexer;
            _rawResourceFactory = rawResourceFactory;
            _resourceIdProvider = resourceIdProvider;
            _configuration = configuration;
            _bulkImportTaskInput = bulkImportTaskInput;
        }

        public async Task ExecuteAsync(IProgress<string> contextProgress, CancellationToken cancellationToken)
        {
            Channel<string> rawDataChannel = Channel.CreateBounded<string>(3000);
            Channel<BulkCopyResourceWrapper> processedResourceChannel = Channel.CreateBounded<BulkCopyResourceWrapper>(3000);
            Channel<BulkCopySearchParamWrapper> searchParamChannel = Channel.CreateBounded<BulkCopySearchParamWrapper>(3000);

            ModelProvider provider = ModelProvider.CreateModelProvider();
            string clientId = _configuration["ClientId"];
            string tenantId = _configuration["TenantId"];
            string secret = _configuration["Secret"];
            LoadRawDataStep loadRawDataStep = new LoadRawDataStep(rawDataChannel, new Uri(_bulkImportTaskInput.BlobLocation), GetClientSecretCredential(tenantId, clientId, secret));
            ProcessFhirResourceStep processFhirResourceStep = new ProcessFhirResourceStep(rawDataChannel, processedResourceChannel, searchParamChannel, _searchIndexer, _rawResourceFactory, _bulkImportTaskInput.StartSurrogateId);
            ImportResourceTableStep importResourceTableStep = new ImportResourceTableStep(processedResourceChannel, _resourceIdProvider, provider, _configuration);
            ImportSearchParamStep importToStringSearchParamTableStep = new ImportSearchParamStep(searchParamChannel, provider, _configuration);

            BulkImportTaskContext context = new BulkImportTaskContext();
            importToStringSearchParamTableStep.Start(new Progress<long>((count) =>
                                                    {
                                                        context.ImportedSearchParamCount = count;
                                                        contextProgress.Report(JsonConvert.SerializeObject(context));
                                                    }));
            processFhirResourceStep.Start(new Progress<long>((count) =>
                                        {
                                            context.ProcessedResourceCount = count;
                                            contextProgress.Report(JsonConvert.SerializeObject(context));
                                        }));
            importResourceTableStep.Start(new Progress<long>((count) =>
                                        {
                                            context.ImportedResourceCount = count;
                                            contextProgress.Report(JsonConvert.SerializeObject(context));
                                        }));
            loadRawDataStep.Start(new Progress<long>());

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
