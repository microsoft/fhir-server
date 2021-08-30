﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.IO;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    public class ImportErrorStore : IImportErrorStore
    {
        private IIntegrationDataStoreClient _integrationDataStoreClient;
        private Uri _fileUri;
        private RecyclableMemoryStreamManager _recyclableMemoryStreamManager;

        public ImportErrorStore(IIntegrationDataStoreClient integrationDataStoreClient, Uri fileUri)
        {
            EnsureArg.IsNotNull(integrationDataStoreClient, nameof(integrationDataStoreClient));
            EnsureArg.IsNotNull(fileUri, nameof(fileUri));

            _integrationDataStoreClient = integrationDataStoreClient;
            _fileUri = fileUri;

            _recyclableMemoryStreamManager = new RecyclableMemoryStreamManager();
        }

        public string ErrorFileLocation => _fileUri.ToString();

        /// <summary>
        /// Upload error logs to store. Append to existed error file.
        /// </summary>
        /// <param name="importErrors">New import errors</param>
        /// <param name="cancellationToken">Cancellaltion Token</param>
        public async Task UploadErrorsAsync(string[] importErrors, CancellationToken cancellationToken)
        {
            if (importErrors == null || importErrors.Length == 0)
            {
                return;
            }

            using var stream = new RecyclableMemoryStream(_recyclableMemoryStreamManager);
            using StreamWriter writer = new StreamWriter(stream);

            foreach (string error in importErrors)
            {
                await writer.WriteLineAsync(error);
            }

            await writer.FlushAsync();
            stream.Position = 0;

            string blockId = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
            await _integrationDataStoreClient.UploadBlockAsync(_fileUri, stream, blockId, cancellationToken);
            await _integrationDataStoreClient.AppendCommitAsync(_fileUri, new string[] { blockId }, cancellationToken);
        }
    }
}
