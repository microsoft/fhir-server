// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.ExportDestinationClient;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export
{
    /// <summary>
    /// Helper class that takes care of managing files for export. Currently it takes
    /// care of creating new files (at the beginning of export as well as once a file
    /// has reached a certain size) and keeping track of current file for each resource type.
    /// Expected use case: each ExportJobTask will instantiate a copy; single-threaded access.
    /// </summary>
    internal class ExportFileManager
    {
        private readonly ExportJobRecord _exportJobRecord;
        private readonly IExportDestinationClient _exportDestinationClient;
        private readonly IDictionary<string, ExportFileInfo> _resourceTypeToFileInfoMapping;
        private readonly uint _approxMaxFileSizeInBytes;
        private bool _isInitialized = false;
        private Dictionary<string, bool> _resourceCommited = new Dictionary<string, bool>();

        public ExportFileManager(ExportJobRecord exportJobRecord, IExportDestinationClient exportDestinationClient)
        {
            EnsureArg.IsNotNull(exportJobRecord, nameof(exportJobRecord));
            EnsureArg.IsNotNull(exportDestinationClient, nameof(exportDestinationClient));

            _exportJobRecord = exportJobRecord;
            _exportDestinationClient = exportDestinationClient;
            _approxMaxFileSizeInBytes = _exportJobRecord.RollingFileSizeInMB * 1024 * 1024;
            _resourceTypeToFileInfoMapping = new Dictionary<string, ExportFileInfo>();
        }

        private void Initialize()
        {
            // Each resource type can have multiple files. We need to keep track of the latest file.
            foreach (KeyValuePair<string, List<ExportFileInfo>> output in _exportJobRecord.Output)
            {
                int maxSequence = -1;   // sequence can start with 0
                ExportFileInfo latestFile = null;
                foreach (ExportFileInfo file in output.Value)
                {
                    if (file.Sequence > maxSequence)
                    {
                        maxSequence = file.Sequence;
                        latestFile = file;
                    }
                }

                _resourceTypeToFileInfoMapping.Add(output.Key, latestFile);
                _resourceCommited.Add(output.Key, true);
            }

            _isInitialized = true;
        }

        public void WriteToFile(string resourceType, string data)
        {
            EnsureArg.IsNotNullOrWhiteSpace(resourceType, nameof(resourceType));

            if (!_isInitialized)
            {
                Initialize();
            }

            ExportFileInfo fileInfo = GetFile(resourceType);

            if (_resourceCommited.TryGetValue(resourceType, out var isCommited) && isCommited)
            {
                fileInfo = CreateNewFileAndUpdateMappings(resourceType, fileInfo.Sequence + 1);
            }

            _exportDestinationClient.WriteFilePart(fileInfo.FileUri.OriginalString, data);
            fileInfo.IncrementCount(data.Length);
        }

        public void CommitFullFiles(long cutoffSize)
        {
            // Goes through files and commits any that are at or past the cutoff size in bytes
            // This should be called during compartment searches, the normall commit won't be called until a page of Patients is done
            // This will cut down on the number of files created (hopefully)

            foreach (var resourceType in _resourceTypeToFileInfoMapping.Keys)
            {
                var fileInfo = _resourceTypeToFileInfoMapping[resourceType];
                if (!_resourceCommited[resourceType] && fileInfo.CommittedBytes > cutoffSize)
                {
                    var blobUri = _exportDestinationClient.CommitFile(fileInfo.FileUri.OriginalString);
                    _resourceTypeToFileInfoMapping[resourceType].FileUri = blobUri;
                    _resourceCommited[resourceType] = true;
                }
            }
        }

        public void CommitFiles()
        {
            var blobUris = _exportDestinationClient.Commit();

            foreach (var resourceType in _resourceTypeToFileInfoMapping.Keys)
            {
                // While processing the data we use the filename as a placeholder for the final uri as the file isn't created until the data is commited.
                var fileName = _resourceTypeToFileInfoMapping[resourceType].FileUri.OriginalString;
                if (blobUris.ContainsKey(fileName))
                {
                    _resourceTypeToFileInfoMapping[resourceType].FileUri = blobUris[fileName];
                }

                _resourceCommited[resourceType] = true;
            }
        }

        private ExportFileInfo GetFile(string resourceType)
        {
            EnsureArg.IsNotNullOrWhiteSpace(resourceType, nameof(resourceType));

            if (!_resourceTypeToFileInfoMapping.TryGetValue(resourceType, out ExportFileInfo exportFileInfo))
            {
                // If it is not present in the mapping, we have to create the first file for this resource type.
                // Hence file sequence will always be 1 in this case.
                exportFileInfo = CreateNewFileAndUpdateMappings(resourceType, fileSequence: 1);
            }

            return exportFileInfo;
        }

        private ExportFileInfo CreateNewFileAndUpdateMappings(string resourceType, int fileSequence)
        {
            string fileName;
            if (_exportJobRecord.SchemaVersion == 1)
            {
                fileName = $"{_exportJobRecord.ExportFormat}.ndjson";
            }
            else
            {
                fileName = $"{_exportJobRecord.ExportFormat}-{fileSequence}.ndjson";
            }

            string dateTime = _exportJobRecord.QueuedTime.UtcDateTime.ToString("s")
                    .Replace("-", string.Empty, StringComparison.OrdinalIgnoreCase)
                    .Replace(":", string.Empty, StringComparison.OrdinalIgnoreCase);

            fileName = fileName.Replace(ExportFormatTags.Timestamp, dateTime, StringComparison.OrdinalIgnoreCase);
            fileName = fileName.Replace(ExportFormatTags.Id, _exportJobRecord.Id, StringComparison.OrdinalIgnoreCase);
            fileName = fileName.Replace(ExportFormatTags.ResourceName, resourceType, StringComparison.OrdinalIgnoreCase);

            var newFile = new ExportFileInfo(resourceType, new Uri(fileName, UriKind.Relative), sequence: fileSequence);

            // Since we created a new file the ExportJobRecord Output also needs to be updated.
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
                _resourceCommited[resourceType] = false;
            }
            else
            {
                _resourceTypeToFileInfoMapping.Add(resourceType, newFile);
                _resourceCommited.Add(resourceType, false);
            }

            return newFile;
        }
    }
}
