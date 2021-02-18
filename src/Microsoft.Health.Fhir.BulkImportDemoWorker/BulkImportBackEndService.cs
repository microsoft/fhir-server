// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using Azure.Identity;
using CsvHelper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.ValueSets;
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
            Channel<string> rawDataChannel = Channel.CreateUnbounded<string>();
            Channel<BulkCopyResourceWrapper> processedResourceChannel = Channel.CreateUnbounded<BulkCopyResourceWrapper>();
            Channel<BulkCopySearchParamWrapper> stringSearchParamsChannel = Channel.CreateUnbounded<BulkCopySearchParamWrapper>();
            Dictionary<SearchParamType, Channel<BulkCopySearchParamWrapper>> searchParamChannels = new Dictionary<SearchParamType, Channel<BulkCopySearchParamWrapper>>()
            {
                { SearchParamType.String, stringSearchParamsChannel},
            };

            string clientId = _configuration["ClientId"];
            string tenantId = _configuration["TenantId"];
            string secret = _configuration["Secret"];
            LoadRawDataStep loadRawDataStep = new LoadRawDataStep(rawDataChannel, new Uri("https://adfasia.blob.core.windows.net/synthea/test1/Patient.ndjson"), GetClientSecretCredential(tenantId, clientId, secret));
            ProcessFhirResourceStep processFhirResourceStep = new ProcessFhirResourceStep(rawDataChannel, processedResourceChannel, searchParamChannels, _searchIndexer);
            ImportResourceTableStep importResourceTableStep = new ImportResourceTableStep(processedResourceChannel, _rawResourceFactory, _resourceIdProvider, BuildResourceTypeMapping(), _configuration);
            ImportToStringSearchParamTableStep importToStringSearchParamTableStep = new ImportToStringSearchParamTableStep(stringSearchParamsChannel, BuildResourceTypeMapping(), _configuration);

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

        private static Dictionary<string, short> BuildResourceTypeMapping()
        {
            using var reader = new StreamReader("ResourceTypes.csv");
            using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
            return csv.GetRecords<ResourceType>().ToDictionary(t => t.Name, t => t.ResourceTypeId);
        }
    }
}
