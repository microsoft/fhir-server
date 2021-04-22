// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    public class ImportErrorsManager : IImportErrorUploader
    {
        private const int DefaultMaxBatchSize = 1000;

        private IIntegrationDataStoreClient _integrationDataStoreClient;
        private IImportErrorSerializer _importErrorSerializer;
        private ConcurrentQueue<ProcessError> _processErrors;
        private ILogger<ImportErrorsManager> _logger;

        public ImportErrorsManager(IIntegrationDataStoreClient integrationDataStoreClient, IImportErrorSerializer importErrorSerializer, ILogger<ImportErrorsManager> logger)
        {
            _integrationDataStoreClient = integrationDataStoreClient;
            _importErrorSerializer = importErrorSerializer;
            _logger = logger;

            _processErrors = new ConcurrentQueue<ProcessError>();
        }

        public int MaxBatchSize { get; set; } = DefaultMaxBatchSize;

        public void Add(ProcessError processError)
        {
            _processErrors.Enqueue(processError);
        }

        public Task<(long count, Uri fileUri)> HandleImportErrorAsync(string fileName, Channel<BatchProcessErrorRecord> errorsChannel, long startErrorLogBatchId, Action<long, long> progressUpdater, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public async Task WriteErrorsAsync(Uri errorFileUri, long? endSurrogatedId, CancellationToken cancellationToken)
        {
            List<ProcessError> errorBuffer = new List<ProcessError>();

            List<string> blockIds = new List<string>();
            string blockId;
            ProcessError error;
            while (_processErrors.TryPeek(out error) && (endSurrogatedId == null || error.ResourceSurrogatedId <= endSurrogatedId))
            {
                _processErrors.TryDequeue(out error);

                errorBuffer.Add(error);
                if (errorBuffer.Count < MaxBatchSize)
                {
                    continue;
                }

                blockId = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
                await ProcessErrorsAsync(errorFileUri, errorBuffer.ToArray(), blockId, cancellationToken);
                errorBuffer.Clear();
                blockIds.Add(blockId);
            }

            blockId = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
            await ProcessErrorsAsync(errorFileUri, errorBuffer.ToArray(), blockId, cancellationToken);
            blockIds.Add(blockId);

            await _integrationDataStoreClient.AppendCommitAsync(errorFileUri, blockIds.ToArray(), cancellationToken);
        }

        private async Task ProcessErrorsAsync(Uri fileUri, ProcessError[] errors, string blockId, CancellationToken cancellationToken)
        {
            using MemoryStream stream = new MemoryStream();
            using StreamWriter writer = new StreamWriter(stream);

            foreach (ProcessError error in errors)
            {
                await writer.WriteLineAsync(_importErrorSerializer.Serialize(error));
            }

            stream.Position = 0;
            await _integrationDataStoreClient.UploadBlockAsync(fileUri, stream, blockId, cancellationToken);
        }
    }
}
