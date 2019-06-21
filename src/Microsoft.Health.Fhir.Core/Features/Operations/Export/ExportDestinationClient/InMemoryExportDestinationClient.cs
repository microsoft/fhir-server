// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export.ExportDestinationClient
{
    public class InMemoryExportDestinationClient : IExportDestinationClient
    {
        private Dictionary<Uri, StringBuilder> _exportedData = new Dictionary<Uri, StringBuilder>();
        private Dictionary<(Uri FileUri, uint PartId), Stream> _streamMappings = new Dictionary<(Uri FileUri, uint PartId), Stream>();

        public string DestinationType => "in-memory";

        public async Task ConnectAsync(string destinationConnectionString, CancellationToken cancellationToken, string containerId = null)
        {
            await Task.CompletedTask;
        }

        public async Task<Uri> CreateFileAsync(string fileName, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNullOrWhiteSpace(fileName, nameof(fileName));

            var fileUri = new Uri(fileName, UriKind.Relative);

            if (!_exportedData.ContainsKey(fileUri))
            {
                _exportedData.Add(fileUri, new StringBuilder());
            }

            return await Task.FromResult(fileUri);
        }

        public async Task WriteFilePartAsync(Uri fileUri, uint partId, byte[] bytes, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(fileUri, nameof(fileUri));
            EnsureArg.IsNotNull(bytes, nameof(bytes));

            var key = (fileUri, partId);

            if (!_streamMappings.TryGetValue(key, out Stream stream))
            {
                stream = new MemoryStream();
                _streamMappings.Add(key, stream);
            }

            await stream.WriteAsync(bytes, cancellationToken);
        }

        public async Task CommitAsync(CancellationToken cancellationToken)
        {
            foreach (KeyValuePair<(Uri, uint), Stream> mapping in _streamMappings)
            {
                Stream stream = mapping.Value;

                // Reset the position.
                stream.Position = 0;

                StringBuilder stringBuilder = _exportedData[mapping.Key.Item1];

                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    stringBuilder.Append(await reader.ReadToEndAsync());
                }
            }

            // Now that all of the parts are committed, remove all stream mappings.
            _streamMappings.Clear();
        }

        public Task OpenFileAsync(Uri fileUri, CancellationToken cancellationToken)
        {
            if (!_exportedData.ContainsKey(fileUri))
            {
                _exportedData.Add(fileUri, new StringBuilder());
            }

            return Task.CompletedTask;
        }

        public string GetExportedData(Uri fileUri)
        {
            if (_exportedData.TryGetValue(fileUri, out StringBuilder sb))
            {
                return sb.ToString();
            }

            return null;
        }
    }
}
