// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.ExportDestinationClient;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.Export
{
    public class InMemoryExportDestinationClient : IExportDestinationClient
    {
        private Dictionary<string, StringBuilder> _exportedData = new Dictionary<string, StringBuilder>();
        private Dictionary<string, StringBuilder> _dataBuffers = new Dictionary<string, StringBuilder>();

        public int ExportedDataFileCount => _exportedData.Keys.Count;

        public string ConnectedContainer { get; private set; }

        public async Task ConnectAsync(CancellationToken cancellationToken, string containerId = null)
        {
            ConnectedContainer = containerId;
            await Task.CompletedTask;
        }

        public async Task ConnectAsync(ExportJobConfiguration exportJobConfiguration, CancellationToken cancellationToken, string containerId = null)
        {
            ConnectedContainer = containerId;
            await Task.CompletedTask;
        }

        public void WriteFilePart(string fileName, string data)
        {
            EnsureArg.IsNotNull(fileName, nameof(fileName));
            EnsureArg.IsNotNull(data, nameof(data));

            if (!_dataBuffers.ContainsKey(fileName))
            {
                _dataBuffers.Add(fileName, new StringBuilder());
            }

            _dataBuffers[fileName].Append(data);
        }

        public IDictionary<string, Uri> Commit()
        {
            Dictionary<string, Uri> localUris = new Dictionary<string, Uri>();

            foreach (string fileName in _dataBuffers.Keys)
            {
                var uri = CommitFile(fileName);
                localUris.Add(fileName, uri);
            }

            return localUris;
        }

        public Uri CommitFile(string fileName)
        {
            if (_dataBuffers.ContainsKey(fileName))
            {
                var localStorage = new StringBuilder();
                var data = _dataBuffers.GetValueOrDefault(fileName);
                localStorage.Append(data.ToString());

                _exportedData[fileName] = localStorage;
                _dataBuffers.Remove(fileName);

                return new Uri(fileName, UriKind.Relative);
            }

            return null;
        }

        public string GetExportedData(string fileName)
        {
            if (_exportedData.TryGetValue(fileName, out StringBuilder sb))
            {
                return sb.ToString();
            }

            return null;
        }
    }
}
