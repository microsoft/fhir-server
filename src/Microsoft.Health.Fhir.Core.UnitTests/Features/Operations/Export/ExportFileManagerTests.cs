// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.ExportDestinationClient;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.Export
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Export)]
    public class ExportFileManagerTests
    {
        private ExportFileManager _exportFileManager;
        private ExportJobRecord _exportJobRecord;
        private IExportDestinationClient _exportDestinationClient;
        private readonly string _exportJobConfigurationFormat;

        public ExportFileManagerTests()
        {
            _exportDestinationClient = Substitute.For<IExportDestinationClient>();

            _exportJobConfigurationFormat = $"{ExportFormatTags.ResourceName}";
            _exportJobRecord = new ExportJobRecord(
                new Uri("https://localhost/$export"),
                ExportJobType.All,
                _exportJobConfigurationFormat,
                resourceType: null,
                filters: null,
                "hash",
                rollingFileSizeInMB: 1);

            _exportFileManager = new ExportFileManager(_exportJobRecord, _exportDestinationClient);
        }

        [Fact]
        public void GivenMultipleFilesInOrderForResourceTypeInOutput_WhenWriteToFile_ThenWritesToLatestFile()
        {
            string resourceType = "Patient";
            ExportFileInfo file1 = new ExportFileInfo(resourceType, new Uri("https://localhost/Patient-1.ndjson"), sequence: 1);
            ExportFileInfo file2 = new ExportFileInfo(resourceType, new Uri("https://localhost/Patient-2.ndjson"), sequence: 2);
            ExportFileInfo file3 = new ExportFileInfo(resourceType, new Uri("https://localhost/Patient-3.ndjson"), sequence: 3);

            _exportJobRecord.Output.Add(resourceType, new List<ExportFileInfo>() { file1, file2, file3 });

            _exportFileManager.WriteToFile(resourceType, "test");

            _exportDestinationClient.Received(1).WriteFilePart(Arg.Is("Patient-4.ndjson"), Arg.Any<string>());
        }

        [Fact]
        public void GivenMultipleFilesOutOfOrderForResourceTypeInOutput_WhenWriteToFile_ThenWritesToLatestFile()
        {
            string resourceType = "Patient";
            ExportFileInfo file1 = new ExportFileInfo(resourceType, new Uri("https://localhost/Patient-1.ndjson"), sequence: 1);
            ExportFileInfo file2 = new ExportFileInfo(resourceType, new Uri("https://localhost/Patient-2.ndjson"), sequence: 2);
            ExportFileInfo file3 = new ExportFileInfo(resourceType, new Uri("https://localhost/Patient-3.ndjson"), sequence: 3);

            _exportJobRecord.Output.Add(resourceType, new List<ExportFileInfo>() { file2, file3, file1 });

            _exportFileManager.WriteToFile(resourceType, "test");

            _exportDestinationClient.Received(1).WriteFilePart(Arg.Is("Patient-4.ndjson"), Arg.Any<string>());
        }

        [Fact]
        public void GivenMultipleFilesForMultipleResourceTypeInOutput_WhenWriteToFile_ThenWritesToCorrectLatestFile()
        {
            string resourceType = "Patient";
            ExportFileInfo file1 = new ExportFileInfo(resourceType, new Uri("https://localhost/Patient-1.ndjson"), sequence: 1);
            ExportFileInfo file2 = new ExportFileInfo(resourceType, new Uri("https://localhost/Patient-2.ndjson"), sequence: 2);

            string resourceType2 = "Observation";
            ExportFileInfo file3 = new ExportFileInfo(resourceType2, new Uri("https://localhost/Observation-1.ndjson"), sequence: 1);
            ExportFileInfo file4 = new ExportFileInfo(resourceType2, new Uri("https://localhost/Observation-2.ndjson"), sequence: 2);

            _exportJobRecord.Output.Add(resourceType, new List<ExportFileInfo>() { file2, file1 });
            _exportJobRecord.Output.Add(resourceType2, new List<ExportFileInfo>() { file4, file3 });

            _exportFileManager.WriteToFile(resourceType2, "test");

            _exportDestinationClient.Received(1).WriteFilePart(Arg.Is("Observation-3.ndjson"), Arg.Any<string>());
        }

        [Fact]
        public void GivenNoFilesInOutput_WhenWriteToFile_ThenCreatesNewFile()
        {
            _exportFileManager.WriteToFile("Patient", "test");

            _exportDestinationClient.Received(1).WriteFilePart(Arg.Is("Patient-1.ndjson"), Arg.Any<string>());
            Assert.Single(_exportJobRecord.Output["Patient"]);
        }

        [Fact]
        public void GivenExportJobRecordV1_WhenWriteToFile_ThenNewFileDoesNotHaveSequence()
        {
            InitializeManagerWithV1ExportJobRecord();

            _exportFileManager.WriteToFile("Patient", "test");

            _exportDestinationClient.Received(1).WriteFilePart(Arg.Is("Patient.ndjson"), Arg.Any<string>());
            Assert.Single(_exportJobRecord.Output["Patient"]);
        }

        [Fact]
        public void GivenExportJobRecordV1AndNoRollingFilzeSizeLimit_WhenWriteToFile_ThenDataWrittenToOneFile()
        {
            InitializeManagerWithV1ExportJobRecord();

            string data = "other test";
            _exportFileManager.WriteToFile("Patient", data);
            _exportFileManager.WriteToFile("Patient", data);

            _exportDestinationClient.Received(2).WriteFilePart(Arg.Is("Patient.ndjson"), Arg.Is(data));
            Assert.Single(_exportJobRecord.Output["Patient"]);
        }

        private void InitializeManagerWithV1ExportJobRecord()
        {
            _exportJobRecord = new ExportJobRecord(
               new Uri("https://localhost/$export"),
               ExportJobType.All,
               _exportJobConfigurationFormat,
               resourceType: null,
               filters: null,
               "hash",
               rollingFileSizeInMB: 0,
               schemaVersion: 1);
            _exportFileManager = new ExportFileManager(_exportJobRecord, _exportDestinationClient);
        }
    }
}
