// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    public class ImportErrorUploader : IImportErrorUploader
    {
        private const string LoggingContainer = "logs";
        private const int DefaultMaxBatchSize = 1000;

        private IIntegrationDataStoreClient _integrationDataStoreClient;
        private IImportErrorSerializer _importErrorSerializer;
        private ILogger<ImportErrorUploader> _logger;

        private List<long> _uploadErrorPartIds = new List<long>();

        public ImportErrorUploader(IIntegrationDataStoreClient integrationDataStoreClient, IImportErrorSerializer importErrorSerializer, ILogger<ImportErrorUploader> logger)
        {
            _integrationDataStoreClient = integrationDataStoreClient;
            _importErrorSerializer = importErrorSerializer;
            _logger = logger;
        }

        public int MaxBatchSize { get; set; } = DefaultMaxBatchSize;

        public async Task HandleImportErrorAsync(string fileName, Channel<BatchProcessErrorRecord> errorsChannel, long startErrorLogBatchId, Action<long, long> progressUpdater, CancellationToken cancellationToken)
        {
            _logger.LogInformation($"Start to upload error logs for {fileName}");

            long currentErrorLogBatchId = startErrorLogBatchId;
            Uri fileUri = await _integrationDataStoreClient.PrepareResourceAsync(LoggingContainer, fileName, cancellationToken);
            List<BatchProcessErrorRecord> buffer = new List<BatchProcessErrorRecord>();

            for (long i = 0; i < startErrorLogBatchId; ++i)
            {
                _uploadErrorPartIds.Add(i);
            }

            do
            {
                await foreach (BatchProcessErrorRecord errorRecord in errorsChannel.Reader.ReadAllAsync())
                {
                    buffer.Add(errorRecord);
                    if (buffer.Count < MaxBatchSize)
                    {
                        continue;
                    }

                    currentErrorLogBatchId = await ProcessErrorLogRecordsAsync(currentErrorLogBatchId, fileUri, buffer.ToArray(), progressUpdater, cancellationToken);
                    buffer.Clear();
                }
            }
            while (await errorsChannel.Reader.WaitToReadAsync() && !cancellationToken.IsCancellationRequested);

            await ProcessErrorLogRecordsAsync(currentErrorLogBatchId, fileUri, buffer.ToArray(), progressUpdater, cancellationToken);

            _logger.LogInformation($"Upload error logs for {fileName} completed.");
        }

        private async Task<long> ProcessErrorLogRecordsAsync(long currentErrorLogBatchId, Uri fileUri, BatchProcessErrorRecord[] records, Action<long, long> progressUpdater, CancellationToken cancellationToken)
        {
            long lastSurragatedId = 0;
            using MemoryStream stream = new MemoryStream();
            using StreamWriter writer = new StreamWriter(stream);
            bool isEmpty = true;

            foreach (BatchProcessErrorRecord record in records)
            {
                foreach (ProcessError error in record.ProcessErrors)
                {
                    await writer.WriteLineAsync(_importErrorSerializer.Serialize(error));
                    isEmpty = false;
                }

                await writer.FlushAsync();
                lastSurragatedId = record.LastSurragatedId;
            }

            if (!isEmpty)
            {
                stream.Position = 0;
                _uploadErrorPartIds.Add(currentErrorLogBatchId);
                await _integrationDataStoreClient.UploadPartDataAsync(fileUri, stream, currentErrorLogBatchId, cancellationToken);
                await _integrationDataStoreClient.CommitDataAsync(fileUri, _uploadErrorPartIds.ToArray(), cancellationToken);

                progressUpdater?.Invoke(currentErrorLogBatchId, lastSurragatedId);
                currentErrorLogBatchId++;
            }
            else
            {
                progressUpdater?.Invoke(currentErrorLogBatchId, lastSurragatedId);
            }

            return currentErrorLogBatchId;
        }
    }
}
