// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.ExportDestinationClient;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export
{
    /// <summary>
    /// Helper class that takes care of managing files for export. Currently it takes
    /// care of creating new files (at the beginning of export as well as once a file
    /// has reached a certain size) and keeping track of current file for each resource type.
    /// Expected use case: each ExportJobTask will instantiate a copy.
    /// </summary>
    internal class ExportFileManager
    {
        private readonly ExportJobRecord _exportJobRecord;
        private readonly IExportDestinationClient _exportDestinationClient;
        private readonly IDictionary<string, ExportFileInfo> _resourceTypeToFileInfoMapping;
        private bool _isInitialized = false;

        public ExportFileManager(ExportJobRecord exportJobRecord, IExportDestinationClient exportDestinationClient)
        {
            EnsureArg.IsNotNull(exportJobRecord, nameof(exportJobRecord));
            EnsureArg.IsNotNull(exportDestinationClient, nameof(exportDestinationClient));

            _exportJobRecord = exportJobRecord;
            _exportDestinationClient = exportDestinationClient;
            _resourceTypeToFileInfoMapping = new Dictionary<string, ExportFileInfo>();
        }

        private async Task Initialize()
        {
            // Each resource type can have multiple files. We need to keep track of the latest file.
            foreach (KeyValuePair<string, List<ExportFileInfo>> output in _exportJobRecord.Output)
            {
                ExportFileInfo latestFile = output.Value[output.Value.Count - 1];
                _resourceTypeToFileInfoMapping.Add(output.Key, latestFile);

                // If there are entries in ExportJobRecord Output before FileManager gets initialized,
                // it means we are "restarting" an export job. We have to make sure that each file
                // has been opened on the ExportDestinationClient.
                await _exportDestinationClient.OpenFileAsync(latestFile.FileUri, CancellationToken.None);
            }

            _isInitialized = true;
        }

        public async Task WriteToFile(string resourceType, string partId, byte[] data, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNullOrWhiteSpace(resourceType, nameof(resourceType));

            if (!_isInitialized)
            {
                await Initialize();
            }

            ExportFileInfo fileInfo = await GetFile(resourceType, cancellationToken);
            await _exportDestinationClient.WriteFilePartAsync(fileInfo.FileUri, partId, data, cancellationToken);

            fileInfo.IncrementCount(data.Length);

            // We need to create a new file if the current file has exceeded a certain limit.
            if (fileInfo.CommittedBytes >= 1024 * 1024)
            {
                await CreateNewFileAndUpdateMappings(resourceType, fileInfo.Sequence + 1, cancellationToken);
            }
        }

        private async Task<ExportFileInfo> GetFile(string resourceType, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNullOrWhiteSpace(resourceType, nameof(resourceType));

            if (!_resourceTypeToFileInfoMapping.TryGetValue(resourceType, out ExportFileInfo exportFileInfo))
            {
                exportFileInfo = await CreateNewFileAndUpdateMappings(resourceType, 1, cancellationToken);
            }

            return exportFileInfo;
        }

        private async Task<ExportFileInfo> CreateNewFileAndUpdateMappings(string resourceType, int fileSequence, CancellationToken cancellationToken)
        {
            string fileName = _exportJobRecord.ExportFormat + ".ndjson";

            string dateTime = _exportJobRecord.QueuedTime.UtcDateTime.ToString("s")
                    .Replace("-", string.Empty, StringComparison.OrdinalIgnoreCase)
                    .Replace(":", string.Empty, StringComparison.OrdinalIgnoreCase);

            fileName = fileName.Replace(ExportFormatTags.Timestamp, dateTime, StringComparison.OrdinalIgnoreCase);
            fileName = fileName.Replace(ExportFormatTags.Id, _exportJobRecord.Id, StringComparison.OrdinalIgnoreCase);
            fileName = fileName.Replace(ExportFormatTags.ResourceName, resourceType, StringComparison.OrdinalIgnoreCase);
            fileName = fileName.Replace(ExportFormatTags.Sequence, fileSequence.ToString(), StringComparison.OrdinalIgnoreCase);

            Uri fileUri = await _exportDestinationClient.CreateFileAsync(fileName, cancellationToken);

            var newFile = new ExportFileInfo(resourceType, fileUri, sequence: fileSequence);

            // Since we created a new file the JobRecord Output also needs to know about it.
            if (_exportJobRecord.Output.TryGetValue(resourceType, out List<ExportFileInfo> fileList))
            {
                fileList.Add(newFile);
            }
            else
            {
                _exportJobRecord.Output.Add(resourceType, new List<ExportFileInfo>() { newFile });
            }

            // Update internal mapping with new file for the resource type.
            if (_resourceTypeToFileInfoMapping.ContainsKey(resourceType))
            {
                _resourceTypeToFileInfoMapping[resourceType] = newFile;
            }
            else
            {
                _resourceTypeToFileInfoMapping.Add(resourceType, newFile);
            }

            return newFile;
        }
    }
}
