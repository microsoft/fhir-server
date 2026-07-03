// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Hl7.Fhir.Rest;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Operations.Security;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.Security
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Security)]
    public class AsyncOperationSmartScopeValidatorTests
    {
        // SMART v1 scope actions as produced by the SMART clinical scopes middleware.
        private static readonly DataActions V1ReadActions = DataActions.Read | DataActions.Export | DataActions.Search;
        private static readonly DataActions V1WriteActions = DataActions.Write | DataActions.Create | DataActions.Update | DataActions.Delete;

        // SMART v2 scope actions as produced by the SMART clinical scopes middleware.
        private static readonly DataActions V2ReadByIdActions = DataActions.ReadById; // "r"
        private static readonly DataActions V2SearchActions = DataActions.Search | DataActions.Export; // "s"
        private static readonly DataActions V2ReadSearchActions = DataActions.ReadById | DataActions.Search | DataActions.Export; // "rs"
        private static readonly DataActions V2SearchOnlyActions = DataActions.Search | DataActions.Export; // "s"
        private static readonly DataActions V2CreateActions = DataActions.Create; // "c"
        private static readonly DataActions V2UpdateActions = DataActions.Update; // "u"
        private static readonly DataActions V2DeleteActions = DataActions.Delete; // "d"
        private static readonly DataActions V2CreateUpdateDeleteActions = DataActions.Create | DataActions.Update | DataActions.Delete; // "cud"

        private const string System = "system";
        private const string Patient = "patient";
        private const string ResourceObservation = "Observation";
        private const string ResourcePatient = "Patient";
        private const string ResourceGroup = "Group";

        [Fact]
        public void GivenV1SystemObservationReadScope_WhenValidatingCompletedObservationExport_ThenAccessIsAllowed()
        {
            var validator = CreateValidator(applyFineGrainedAccessControl: true, new ScopeRestriction(ResourceObservation, V1ReadActions, System));

            ExportJobRecord record = CreateExportJobRecordWithOutput(ResourceObservation);

            // Should not throw.
            validator.ValidateExportStatusAccess(record);
        }

        [Fact]
        public void GivenV2SystemObservationReadSearchScope_WhenValidatingCompletedObservationExport_ThenAccessIsAllowed()
        {
            var validator = CreateValidator(applyFineGrainedAccessControl: true, new ScopeRestriction(ResourceObservation, V2ReadSearchActions, System));

            ExportJobRecord record = CreateExportJobRecordWithOutput(ResourceObservation);

            // Should not throw.
            validator.ValidateExportStatusAccess(record);
        }

        [Fact]
        public void GivenV2SystemObservationReadAndSearchScopes_WhenValidatingCompletedObservationExport_ThenAccessIsAllowed()
        {
            var validator = CreateValidator(
                applyFineGrainedAccessControl: true,
                new ScopeRestriction(ResourceObservation, V2ReadByIdActions, System),
                new ScopeRestriction(ResourceObservation, V2SearchActions, System));

            ExportJobRecord record = CreateExportJobRecordWithOutput(ResourceObservation);

            // Should not throw.
            validator.ValidateExportStatusAccess(record);
        }

        [Fact]
        public void GivenV2SystemObservationSearchOnlyScope_WhenValidatingCompletedObservationExport_ThenUnauthorizedIsThrown()
        {
            var validator = CreateValidator(applyFineGrainedAccessControl: true, new ScopeRestriction(ResourceObservation, V2SearchOnlyActions, System));

            ExportJobRecord record = CreateExportJobRecordWithOutput(ResourceObservation);

            Assert.Throws<UnauthorizedFhirActionException>(() => validator.ValidateExportStatusAccess(record));
        }

        [Fact]
        public void GivenV1SystemObservationReadScope_WhenValidatingCompletedPatientExport_ThenUnauthorizedIsThrown()
        {
            var validator = CreateValidator(applyFineGrainedAccessControl: true, new ScopeRestriction(ResourceObservation, V1ReadActions, System));

            ExportJobRecord record = CreateExportJobRecordWithOutput(ResourcePatient);

            Assert.Throws<UnauthorizedFhirActionException>(() => validator.ValidateExportStatusAccess(record));
        }

        [Fact]
        public void GivenSystemAllReadAndSystemAllWriteScopes_WhenValidatingAllResourceReadWrite_ThenAccessIsAllowed()
        {
            var validator = CreateValidator(
                applyFineGrainedAccessControl: true,
                new ScopeRestriction(KnownResourceTypes.All, V1ReadActions, System),
                new ScopeRestriction(KnownResourceTypes.All, V1WriteActions, System));

            // Should not throw.
            validator.ValidateAllResourceReadWriteAccess();
        }

        [Fact]
        public void GivenSystemObservationReadWriteScope_WhenValidatingAllResourceReadWrite_ThenUnauthorizedIsThrown()
        {
            var validator = CreateValidator(
                applyFineGrainedAccessControl: true,
                new ScopeRestriction(ResourceObservation, V1ReadActions | V1WriteActions, System));

            Assert.Throws<UnauthorizedFhirActionException>(() => validator.ValidateAllResourceReadWriteAccess());
        }

        [Fact]
        public void GivenSystemAllReadSearchAndSystemAllCreateUpdateDeleteScopes_WhenValidatingAllResourceReadWrite_ThenAccessIsAllowed()
        {
            var validator = CreateValidator(
                applyFineGrainedAccessControl: true,
                new ScopeRestriction(KnownResourceTypes.All, V2ReadSearchActions, System),
                new ScopeRestriction(KnownResourceTypes.All, V2CreateUpdateDeleteActions, System));

            // Should not throw.
            validator.ValidateAllResourceReadWriteAccess();
        }

        [Fact]
        public void GivenSplitV2SystemAllReadSearchCreateUpdateDeleteScopes_WhenValidatingAllResourceReadWrite_ThenAccessIsAllowed()
        {
            var validator = CreateValidator(
                applyFineGrainedAccessControl: true,
                new ScopeRestriction(KnownResourceTypes.All, V2ReadByIdActions, System),
                new ScopeRestriction(KnownResourceTypes.All, V2SearchActions, System),
                new ScopeRestriction(KnownResourceTypes.All, V2CreateActions, System),
                new ScopeRestriction(KnownResourceTypes.All, V2UpdateActions, System),
                new ScopeRestriction(KnownResourceTypes.All, V2DeleteActions, System));

            // Should not throw.
            validator.ValidateAllResourceReadWriteAccess();
        }

        [Fact]
        public void GivenNonFineGrainedRequest_WhenValidating_ThenBothChecksNoOp()
        {
            // A restricted-looking scope is present, but fine-grained access control is disabled, so the validator no-ops.
            var validator = CreateValidator(
                applyFineGrainedAccessControl: false,
                new ScopeRestriction(ResourceObservation, V1ReadActions, System));

            ExportJobRecord record = CreateExportJobRecordWithOutput(ResourcePatient);

            // Neither call should throw for a non-SMART/non-fine-grained request.
            validator.ValidateExportStatusAccess(record);
            validator.ValidateAllResourceReadWriteAccess();
        }

        [Fact]
        public void GivenFineGrainedRequestWithNoAllowedResourceActions_WhenValidating_ThenBothChecksNoOp()
        {
            var validator = CreateValidator(applyFineGrainedAccessControl: true);

            ExportJobRecord record = CreateExportJobRecordWithOutput(ResourcePatient);

            // With no allowed resource actions the validator treats the request as non-SMART restricted and no-ops.
            validator.ValidateExportStatusAccess(record);
            validator.ValidateAllResourceReadWriteAccess();
        }

        [Theory]
        [InlineData(ExportJobType.Patient)]
        [InlineData(ExportJobType.Group)]
        public void GivenNonAllExportWithNoOutputResourceTypeOrFilters_WhenValidatingWithNonMatchingSystemScope_ThenUnauthorizedIsThrown(ExportJobType exportJobType)
        {
            // Fallback path: a Patient export requires a Patient scope and a Group export requires a Group scope.
            // A system/Observation.read scope does not cover either, so access is denied.
            var validator = CreateValidator(applyFineGrainedAccessControl: true, new ScopeRestriction(ResourceObservation, V1ReadActions, System));

            ExportJobRecord record = CreateExportJobRecordWithoutNarrowing(exportJobType);

            Assert.Throws<UnauthorizedFhirActionException>(() => validator.ValidateExportStatusAccess(record));
        }

        [Fact]
        public void GivenSystemPatientReadScope_WhenValidatingEmptyPatientExport_ThenUnauthorizedIsThrown()
        {
            // A Patient export with no output/_type/_typeFilter may include patient-compartment resources beyond Patient.
            var validator = CreateValidator(applyFineGrainedAccessControl: true, new ScopeRestriction(ResourcePatient, V1ReadActions, System));

            ExportJobRecord record = CreateExportJobRecordWithoutNarrowing(ExportJobType.Patient);

            Assert.Throws<UnauthorizedFhirActionException>(() => validator.ValidateExportStatusAccess(record));
        }

        [Fact]
        public void GivenSystemGroupReadScope_WhenValidatingEmptyGroupExport_ThenUnauthorizedIsThrown()
        {
            // A Group export with no output/_type/_typeFilter may include group-member compartment resources beyond Group.
            var validator = CreateValidator(applyFineGrainedAccessControl: true, new ScopeRestriction(ResourceGroup, V1ReadActions, System));

            ExportJobRecord record = CreateExportJobRecordWithoutNarrowing(ExportJobType.Group);

            Assert.Throws<UnauthorizedFhirActionException>(() => validator.ValidateExportStatusAccess(record));
        }

        [Theory]
        [InlineData(ExportJobType.All)]
        [InlineData(ExportJobType.Patient)]
        [InlineData(ExportJobType.Group)]
        public void GivenSystemAllReadScope_WhenValidatingExportWithoutNarrowing_ThenAccessIsAllowed(ExportJobType exportJobType)
        {
            // Any export with no narrowing requires an all-resource system read scope because outputs can span resource types.
            var validator = CreateValidator(applyFineGrainedAccessControl: true, new ScopeRestriction(KnownResourceTypes.All, V1ReadActions, System));

            ExportJobRecord record = CreateExportJobRecordWithoutNarrowing(exportJobType);

            // Should not throw.
            validator.ValidateExportStatusAccess(record);
        }

        [Fact]
        public void GivenSystemObservationReadScope_WhenValidatingEmptySystemExport_ThenUnauthorizedIsThrown()
        {
            // A system-level (All) export requires an all-resource scope; a single-resource scope is insufficient.
            var validator = CreateValidator(applyFineGrainedAccessControl: true, new ScopeRestriction(ResourceObservation, V1ReadActions, System));

            ExportJobRecord record = CreateExportJobRecordWithoutNarrowing(ExportJobType.All);

            Assert.Throws<UnauthorizedFhirActionException>(() => validator.ValidateExportStatusAccess(record));
        }

        [Fact]
        public void GivenConstrainedSystemObservationReadScope_WhenValidatingUnconstrainedObservationExport_ThenUnauthorizedIsThrown()
        {
            var constrainedSearchParams = new SearchParams().Where("category=laboratory");
            var validator = CreateValidator(applyFineGrainedAccessControl: true, new ScopeRestriction(ResourceObservation, V1ReadActions, System, constrainedSearchParams));

            ExportJobRecord record = CreateExportJobRecordWithOutput(ResourceObservation);

            Assert.Throws<UnauthorizedFhirActionException>(() => validator.ValidateExportStatusAccess(record));
        }

        [Theory]
        [InlineData(ExportJobType.Patient)]
        [InlineData(ExportJobType.Group)]
        public void GivenNonAllExportWithNoOutputResourceTypeOrFilters_WhenValidatingWithSearchOnlyScope_ThenUnauthorizedIsThrown(ExportJobType exportJobType)
        {
            // A search-only scope is not a valid export-read scope, so even the fallback path denies access.
            var validator = CreateValidator(applyFineGrainedAccessControl: true, new ScopeRestriction(ResourceObservation, V2SearchOnlyActions, System));

            ExportJobRecord record = CreateExportJobRecordWithoutNarrowing(exportJobType);

            Assert.Throws<UnauthorizedFhirActionException>(() => validator.ValidateExportStatusAccess(record));
        }

        [Fact]
        public void GivenPatientAllReadAndPatientAllWriteScopes_WhenValidatingAllResourceReadWrite_ThenUnauthorizedIsThrown()
        {
            // Only SMART system scopes satisfy all-resource read/write; patient-scoped all-resource grants must not.
            var validator = CreateValidator(
                applyFineGrainedAccessControl: true,
                new ScopeRestriction(KnownResourceTypes.All, V1ReadActions, Patient),
                new ScopeRestriction(KnownResourceTypes.All, V1WriteActions, Patient));

            Assert.Throws<UnauthorizedFhirActionException>(() => validator.ValidateAllResourceReadWriteAccess());
        }

        [Fact]
        public void GivenPatientObservationReadScope_WhenValidatingCompletedObservationExport_ThenUnauthorizedIsThrown()
        {
            // Export status matching only honors SMART system scopes; a patient-scoped Observation read must not satisfy it.
            var validator = CreateValidator(applyFineGrainedAccessControl: true, new ScopeRestriction(ResourceObservation, V1ReadActions, Patient));

            ExportJobRecord record = CreateExportJobRecordWithOutput(ResourceObservation);

            Assert.Throws<UnauthorizedFhirActionException>(() => validator.ValidateExportStatusAccess(record));
        }

        [Fact]
        public void GivenCompletedExportWithBlankOutputKeyAndPatientResourceType_WhenValidatingWithObservationScope_ThenUnauthorizedIsThrown()
        {
            // Malformed output keys should not make authorization less restrictive; fall back to ResourceType.
            var validator = CreateValidator(applyFineGrainedAccessControl: true, new ScopeRestriction(ResourceObservation, V1ReadActions, System));

            ExportJobRecord record = CreateExportJobRecordWithBlankOutputKey(ResourcePatient);

            Assert.Throws<UnauthorizedFhirActionException>(() => validator.ValidateExportStatusAccess(record));
        }

        [Fact]
        public void GivenCompletedObservationOutputWithPatientResourceTypeMetadata_WhenValidatingWithObservationScope_ThenUnauthorizedIsThrown()
        {
            var validator = CreateValidator(applyFineGrainedAccessControl: true, new ScopeRestriction(ResourceObservation, V1ReadActions, System));

            ExportJobRecord record = CreateExportJobRecordWithOutputAndMetadata(
                resourceType: $"{ResourceObservation},{ResourcePatient}",
                filters: null,
                ResourceObservation);

            Assert.Throws<UnauthorizedFhirActionException>(() => validator.ValidateExportStatusAccess(record));
        }

        [Fact]
        public void GivenCompletedObservationOutputWithPatientTypeFilterMetadata_WhenValidatingWithObservationScope_ThenUnauthorizedIsThrown()
        {
            var validator = CreateValidator(applyFineGrainedAccessControl: true, new ScopeRestriction(ResourceObservation, V1ReadActions, System));

            ExportJobRecord record = CreateExportJobRecordWithOutputAndMetadata(
                resourceType: null,
                filters: new List<ExportJobFilter>
                {
                    new ExportJobFilter(ResourcePatient, new List<Tuple<string, string>>
                    {
                        Tuple.Create("active", "true"),
                    }),
                },
                ResourceObservation);

            Assert.Throws<UnauthorizedFhirActionException>(() => validator.ValidateExportStatusAccess(record));
        }

        private static AsyncOperationSmartScopeValidator CreateValidator(bool applyFineGrainedAccessControl, params ScopeRestriction[] scopeRestrictions)
        {
            var contextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            var requestContext = new FhirRequestContext(
                method: "GET",
                uriString: "http://localhost/",
                baseUriString: "http://localhost/",
                correlationId: "async-smart-scope-test",
                requestHeaders: new Dictionary<string, StringValues>(),
                responseHeaders: new Dictionary<string, StringValues>());

            requestContext.AccessControlContext.ApplyFineGrainedAccessControl = applyFineGrainedAccessControl;
            foreach (ScopeRestriction scopeRestriction in scopeRestrictions)
            {
                requestContext.AccessControlContext.AllowedResourceActions.Add(scopeRestriction);
            }

            contextAccessor.RequestContext = requestContext;

            return new AsyncOperationSmartScopeValidator(contextAccessor);
        }

        private static ExportJobRecord CreateExportJobRecordWithOutput(params string[] resourceTypes)
        {
            var record = new ExportJobRecord(
                new Uri("http://localhost/job/"),
                ExportJobType.Patient,
                ExportFormatTags.ResourceName,
                resourceType: null,
                filters: null,
                hash: "123",
                rollingFileSizeInMB: 64)
            {
                Status = OperationStatus.Completed,
            };

            for (int i = 0; i < resourceTypes.Length; i++)
            {
                string type = resourceTypes[i];
                record.Output.Add(type, new List<ExportFileInfo>
                {
                    new ExportFileInfo(type, new Uri($"http://example.com/{type.ToLowerInvariant()}.ndjson"), sequence: i),
                });
            }

            return record;
        }

        private static ExportJobRecord CreateExportJobRecordWithBlankOutputKey(string resourceType)
        {
            var record = new ExportJobRecord(
                new Uri("http://localhost/job/"),
                ExportJobType.Patient,
                ExportFormatTags.ResourceName,
                resourceType,
                filters: null,
                hash: "123",
                rollingFileSizeInMB: 64)
            {
                Status = OperationStatus.Completed,
            };

            record.Output.Add(string.Empty, new List<ExportFileInfo>
            {
                new ExportFileInfo(resourceType, new Uri($"http://example.com/{resourceType.ToLowerInvariant()}.ndjson"), sequence: 0),
            });

            return record;
        }

        private static ExportJobRecord CreateExportJobRecordWithOutputAndMetadata(string resourceType, IList<ExportJobFilter> filters, params string[] outputResourceTypes)
        {
            var record = new ExportJobRecord(
                new Uri("http://localhost/job/"),
                ExportJobType.Patient,
                ExportFormatTags.ResourceName,
                resourceType,
                filters,
                hash: "123",
                rollingFileSizeInMB: 64)
            {
                Status = OperationStatus.Completed,
            };

            for (int i = 0; i < outputResourceTypes.Length; i++)
            {
                string type = outputResourceTypes[i];
                record.Output.Add(type, new List<ExportFileInfo>
                {
                    new ExportFileInfo(type, new Uri($"http://example.com/{type.ToLowerInvariant()}.ndjson"), sequence: i),
                });
            }

            return record;
        }

        private static ExportJobRecord CreateExportJobRecordWithoutNarrowing(ExportJobType exportJobType)
        {
            // No Output, no ResourceType, and no Filters - exercises the empty required-resource fallback path.
            return new ExportJobRecord(
                new Uri("http://localhost/job/"),
                exportJobType,
                ExportFormatTags.ResourceName,
                resourceType: null,
                filters: null,
                hash: "123",
                rollingFileSizeInMB: 64)
            {
                Status = OperationStatus.Completed,
            };
        }
    }
}
