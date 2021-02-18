// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Channels;
using Azure.Core;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.BulkImportDemoWorker
{
    public class LoadRawDataStep : IStep
    {
        private Channel<string> _outputChannel;
        private Task _runningTask;
        private TokenCredential _credential;
        private Uri _blobUri;

        public LoadRawDataStep(Channel<string> outputChannel, Uri blobUri, TokenCredential credential)
        {
            _outputChannel = outputChannel;
            _credential = credential;
            _blobUri = blobUri;

            Children = new List<IStep>();
        }

        public IReadOnlyCollection<IStep> Children { get; set; }

        public void Start()
        {
            _runningTask = Task.Run(async () =>
            {
                using FhirBlobDataStream fhirBlobDataStream = new FhirBlobDataStream(_blobUri, _credential);
                using StreamReader reader = new StreamReader(fhirBlobDataStream);

                string content = null;
                while (!string.IsNullOrEmpty(content = await reader.ReadLineAsync()))
                {
                    await _outputChannel.Writer.WriteAsync(content);
                }

                _outputChannel.Writer.Complete();
            });
        }

        public async Task WaitForStopAsync()
        {
            await _runningTask;
        }
    }
}
