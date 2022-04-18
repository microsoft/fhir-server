// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.ExportDestinationClient;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.Export
{
    public class InMemoryExportDestinationClient : IExportDestinationClient
    {
        private Dictionary<Uri, StringBuilder> _exportedData = new Dictionary<Uri, StringBuilder>();
        private Dictionary<Uri, StringBuilder> _dataBuffers = new Dictionary<Uri, StringBuilder>();

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

        public void WriteFilePartAsync(Uri fileUri, string data, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(fileUri, nameof(fileUri));
            EnsureArg.IsNotNull(data, nameof(data));

            if (!_dataBuffers.ContainsKey(fileUri))
            {
                _dataBuffers.Add(fileUri, new StringBuilder());
            }

            _dataBuffers[fileUri].Append(data);
        }

        public void Commit()
        {
            foreach (Uri fileUri in _dataBuffers.Keys)
            {
                if (_exportedData.TryGetValue(fileUri, out var localStorage))
                {
                    var data = _dataBuffers.GetValueOrDefault(fileUri);
                    localStorage.AppendLine(data.ToString());
                    data.Clear();
                }
            }
        }

        public void OpenFileAsync(Uri fileUri)
        {
            if (!_exportedData.ContainsKey(fileUri))
            {
                _exportedData.Add(fileUri, new StringBuilder());
            }
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
