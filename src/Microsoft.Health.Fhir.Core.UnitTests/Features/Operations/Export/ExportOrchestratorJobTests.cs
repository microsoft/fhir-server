// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.JobManagement;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.Export
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Export)]
    public class ExportOrchestratorJobTests
    {
        [Fact]
        public void GivenSmartPatientInstanceExport_WhenCreatingProcessingRecord_ThenSecurityContextIsPreserved()
        {
            var coordinatorRecord = new ExportJobRecord(
                new Uri("http://localhost/Patient/123/$export?_type=Patient"),
                ExportJobType.Patient,
                ExportFormatTags.ResourceName,
                "Patient",
                filters: null,
                hash: "hash",
                rollingFileSizeInMB: 64,
                patientId: "123",
                smartCompartmentResourceType: "Patient",
                smartCompartmentId: "123",
                smartRequest: true);

            ExportJobRecord processingRecord = TestExportOrchestratorJob.CreateProcessingRecord(coordinatorRecord);

            Assert.Equal("123", processingRecord.PatientId);
            Assert.Equal("Patient", processingRecord.SmartCompartmentResourceType);
            Assert.Equal("123", processingRecord.SmartCompartmentId);
        }

        private sealed class TestExportOrchestratorJob : ExportOrchestratorJob
        {
            public static ExportJobRecord CreateProcessingRecord(ExportJobRecord record)
            {
                return CreateExportRecord(record, groupId: 1);
            }

            public override Task<string> ExecuteAsync(JobInfo jobInfo, CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }
        }
    }
}
