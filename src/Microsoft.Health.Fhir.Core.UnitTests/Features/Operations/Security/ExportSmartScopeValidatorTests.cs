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
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Operations.Security;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Messages.Export;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.Security
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Security)]
    public class ExportSmartScopeValidatorTests
    {
        private static readonly DataActions V1ExportRead = DataActions.Read | DataActions.Export;
        private static readonly DataActions V2ExportRead = DataActions.ReadById | DataActions.Search | DataActions.Export;

        private readonly RequestContextAccessor<IFhirRequestContext> _contextAccessor = new FhirRequestContextAccessor();

        [Theory]
        [InlineData("patient")]
        [InlineData("user")]
        public void GivenPatientOrUserScope_WhenCreatingExport_ThenForbiddenIsThrown(string scopeContext)
        {
            ExportSmartScopeValidator validator = CreateValidator(
                new ScopeRestriction(KnownResourceTypes.Patient, V1ExportRead, scopeContext));

            Assert.Throws<UnauthorizedFhirActionException>(() =>
                validator.ValidateCreateAccess(CreateExportRequest(KnownResourceTypes.Patient)));
        }

        [Fact]
        public void GivenFineGrainedContextWithoutScopeRestrictions_WhenValidatingExportAccess_ThenForbiddenIsThrown()
        {
            // Fine-grained access can be enabled before scopes are parsed, so an empty result must fail closed.
            ExportSmartScopeValidator validator = CreateValidator();

            Assert.Throws<UnauthorizedFhirActionException>(() =>
                validator.ValidateCreateAccess(CreateExportRequest(KnownResourceTypes.Patient)));
            Assert.Throws<UnauthorizedFhirActionException>(() =>
                validator.ValidateJobAccess(CreateExportJobRecord(KnownResourceTypes.Patient)));

            // Without any explicit _type, an empty scope set has no eligible resource types to infer either.
            Assert.Throws<UnauthorizedFhirActionException>(() =>
                validator.ValidateCreateAccess(CreateExportRequest(resourceType: null)));
        }

        [Fact]
        public void GivenMixedPatientAndSystemScopes_WhenValidatingExportAccess_ThenOnlySystemScopeAuthorizesTypes()
        {
            // Patient or user scopes may coexist with system scopes but must never broaden export access.
            ExportSmartScopeValidator validator = CreateValidator(
                new ScopeRestriction(KnownResourceTypes.Observation, V1ExportRead, "patient"),
                new ScopeRestriction(KnownResourceTypes.Patient, V1ExportRead, "system"));

            validator.ValidateCreateAccess(CreateExportRequest(KnownResourceTypes.Patient));
            Assert.Throws<UnauthorizedFhirActionException>(() =>
                validator.ValidateCreateAccess(CreateExportRequest(KnownResourceTypes.Observation)));
        }

        [Theory]
        [InlineData(DataActions.Read | DataActions.Export)]
        [InlineData(DataActions.ReadById | DataActions.Search | DataActions.Export)]
        public void GivenSystemWildcardExportReadScope_WhenCreatingExportWithoutType_ThenAccessIsAllowedAndTypeStaysUnconstrained(DataActions actions)
        {
            ExportSmartScopeValidator validator = CreateValidator(
                new ScopeRestriction(KnownResourceTypes.All, actions, "system"));

            string effectiveResourceType = validator.ValidateCreateAccess(CreateExportRequest(resourceType: null));

            Assert.Null(effectiveResourceType);
        }

        [Fact]
        public void GivenSinglePartialSystemScope_WhenCreatingExportWithoutType_ThenEffectiveTypeIsInferredAndNarrowed()
        {
            // A partial system scope with complete export-read actions now narrows the export instead of being forbidden.
            ExportSmartScopeValidator validator = CreateValidator(
                new ScopeRestriction(KnownResourceTypes.Patient, V1ExportRead, "system"));

            string effectiveResourceType = validator.ValidateCreateAccess(CreateExportRequest(resourceType: null));

            Assert.Equal(KnownResourceTypes.Patient, effectiveResourceType);
        }

        [Fact]
        public void GivenMultiplePartialSystemScopes_WhenCreatingExportWithoutType_ThenEffectiveTypeIncludesEveryEligibleType()
        {
            ExportSmartScopeValidator validator = CreateValidator(
                new ScopeRestriction(KnownResourceTypes.Patient, V1ExportRead, "system"),
                new ScopeRestriction(KnownResourceTypes.Observation, V2ExportRead, "system"));

            string effectiveResourceType = validator.ValidateCreateAccess(CreateExportRequest(resourceType: null));

            Assert.Equal("Observation,Patient", effectiveResourceType);
        }

        [Theory]
        [InlineData(DataActions.Export)]
        [InlineData(DataActions.Read)]
        [InlineData(DataActions.ReadById | DataActions.Export)]
        [InlineData(DataActions.Search | DataActions.Export)]
        public void GivenOnlyIncompleteSystemScopeActions_WhenCreatingExportWithoutType_ThenForbiddenIsThrown(DataActions actions)
        {
            // No resource-specific scope has complete export-read actions, so there is nothing eligible to infer.
            ExportSmartScopeValidator validator = CreateValidator(
                new ScopeRestriction(KnownResourceTypes.Patient, actions, "system"));

            Assert.Throws<UnauthorizedFhirActionException>(() =>
                validator.ValidateCreateAccess(CreateExportRequest(resourceType: null)));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" , ")]
        public void GivenPartialSystemScope_WhenCreatingExportWithEffectivelyEmptyType_ThenEffectiveTypeIsInferred(string resourceType)
        {
            ExportSmartScopeValidator validator = CreateValidator(
                new ScopeRestriction(KnownResourceTypes.Patient, V1ExportRead, "system"));

            string effectiveResourceType = validator.ValidateCreateAccess(CreateExportRequest(resourceType));

            Assert.Equal(KnownResourceTypes.Patient, effectiveResourceType);
        }

        [Theory]
        [InlineData(DataActions.Read | DataActions.Export)]
        [InlineData(DataActions.ReadById | DataActions.Search | DataActions.Export)]
        public void GivenMatchingSystemExportReadScope_WhenCreatingExportWithExplicitType_ThenAccessIsAllowedAndTypeIsPreserved(DataActions actions)
        {
            ExportSmartScopeValidator validator = CreateValidator(
                new ScopeRestriction(KnownResourceTypes.Patient, actions, "system"));

            string effectiveResourceType = validator.ValidateCreateAccess(CreateExportRequest(KnownResourceTypes.Patient));

            Assert.Equal(KnownResourceTypes.Patient, effectiveResourceType);
        }

        [Fact]
        public void GivenResourceTypeCaseDiffersFromSystemScope_WhenCreatingExport_ThenAccessIsAllowed()
        {
            ExportSmartScopeValidator validator = CreateValidator(
                new ScopeRestriction("patient", V1ExportRead, "system"));

            validator.ValidateCreateAccess(CreateExportRequest("PATIENT"));
        }

        [Fact]
        public void GivenSystemScopesCoveringEveryRequestedType_WhenCreatingExportWithExplicitTypes_ThenAccessIsAllowedAndSubsetIsPreserved()
        {
            ExportSmartScopeValidator validator = CreateValidator(
                new ScopeRestriction(KnownResourceTypes.Patient, V1ExportRead, "system"),
                new ScopeRestriction(KnownResourceTypes.Observation, V2ExportRead, "system"));

            string effectiveResourceType = validator.ValidateCreateAccess(CreateExportRequest("Patient,Observation"));

            Assert.Equal("Patient,Observation", effectiveResourceType);
        }

        [Fact]
        public void GivenSystemScopeMissingRequestedType_WhenCreatingExportWithExplicitTypes_ThenForbiddenIsThrown()
        {
            ExportSmartScopeValidator validator = CreateValidator(
                new ScopeRestriction(KnownResourceTypes.Patient, V1ExportRead, "system"));

            Assert.Throws<UnauthorizedFhirActionException>(() =>
                validator.ValidateCreateAccess(CreateExportRequest("Patient,Observation")));
        }

        [Theory]
        [InlineData(DataActions.Export)]
        [InlineData(DataActions.Read)]
        [InlineData(DataActions.ReadById | DataActions.Export)]
        [InlineData(DataActions.Search | DataActions.Export)]
        public void GivenIncompleteSystemScopeActions_WhenCreatingExport_ThenForbiddenIsThrown(DataActions actions)
        {
            ExportSmartScopeValidator validator = CreateValidator(
                new ScopeRestriction(KnownResourceTypes.Patient, actions, "system"));

            Assert.Throws<UnauthorizedFhirActionException>(() =>
                validator.ValidateCreateAccess(CreateExportRequest(KnownResourceTypes.Patient)));
        }

        [Fact]
        public void GivenSearchParameterConstrainedSystemScope_WhenCreatingExport_ThenForbiddenIsThrown()
        {
            // Bulk export cannot safely enforce scope search constraints, so constrained scopes do not authorize it.
            ExportSmartScopeValidator validator = CreateValidator(
                new ScopeRestriction(
                    KnownResourceTypes.Patient,
                    V2ExportRead,
                    "system",
                    new SearchParams("active", "true")));

            Assert.Throws<UnauthorizedFhirActionException>(() =>
                validator.ValidateCreateAccess(CreateExportRequest(KnownResourceTypes.Patient)));
        }

        [Fact]
        public void GivenOnlySearchParameterConstrainedSystemScope_WhenCreatingExportWithoutType_ThenForbiddenIsThrown()
        {
            // A search-parameter-constrained scope is never eligible for inference either.
            ExportSmartScopeValidator validator = CreateValidator(
                new ScopeRestriction(
                    KnownResourceTypes.Patient,
                    V2ExportRead,
                    "system",
                    new SearchParams("active", "true")));

            Assert.Throws<UnauthorizedFhirActionException>(() =>
                validator.ValidateCreateAccess(CreateExportRequest(resourceType: null)));
        }

        [Fact]
        public void GivenPatientRouteWithoutSystemPatientAccess_WhenCreatingExportWithExplicitType_ThenForbiddenIsThrown()
        {
            // Patient/$export?_type=Observation must require system/Patient in addition to the explicit output type.
            ExportSmartScopeValidator validator = CreateValidator(
                new ScopeRestriction(KnownResourceTypes.Observation, V1ExportRead, "system"));

            Assert.Throws<UnauthorizedFhirActionException>(() =>
                validator.ValidateCreateAccess(CreateExportRequest(KnownResourceTypes.Observation, ExportJobType.Patient)));
        }

        [Fact]
        public void GivenPatientRouteWithSystemPatientAndOutputTypeAccess_WhenCreatingExportWithExplicitType_ThenAccessIsAllowed()
        {
            ExportSmartScopeValidator validator = CreateValidator(
                new ScopeRestriction(KnownResourceTypes.Observation, V1ExportRead, "system"),
                new ScopeRestriction(KnownResourceTypes.Patient, V1ExportRead, "system"));

            string effectiveResourceType = validator.ValidateCreateAccess(
                CreateExportRequest(KnownResourceTypes.Observation, ExportJobType.Patient));

            Assert.Equal(KnownResourceTypes.Observation, effectiveResourceType);
        }

        [Fact]
        public void GivenGroupRouteWithOnlySystemPatientAccess_WhenCreatingExportWithExplicitType_ThenForbiddenIsThrown()
        {
            // Group/{id}/$export?_type=Patient must require both system/Group and system/Patient.
            ExportSmartScopeValidator validator = CreateValidator(
                new ScopeRestriction(KnownResourceTypes.Patient, V1ExportRead, "system"));

            Assert.Throws<UnauthorizedFhirActionException>(() =>
                validator.ValidateCreateAccess(CreateExportRequest(KnownResourceTypes.Patient, ExportJobType.Group)));
        }

        [Fact]
        public void GivenGroupRouteWithSystemGroupAndPatientAccess_WhenCreatingExportWithExplicitType_ThenAccessIsAllowed()
        {
            ExportSmartScopeValidator validator = CreateValidator(
                new ScopeRestriction(KnownResourceTypes.Group, V1ExportRead, "system"),
                new ScopeRestriction(KnownResourceTypes.Patient, V1ExportRead, "system"));

            string effectiveResourceType = validator.ValidateCreateAccess(
                CreateExportRequest(KnownResourceTypes.Patient, ExportJobType.Group));

            Assert.Equal(KnownResourceTypes.Patient, effectiveResourceType);
        }

        [Fact]
        public void GivenGroupRouteWithOnlySystemPatientAccess_WhenCreatingExportWithoutType_ThenForbiddenIsThrown()
        {
            // Route prerequisites must be enforced even when the effective _type is being inferred (no explicit _type).
            ExportSmartScopeValidator validator = CreateValidator(
                new ScopeRestriction(KnownResourceTypes.Patient, V1ExportRead, "system"));

            Assert.Throws<UnauthorizedFhirActionException>(() =>
                validator.ValidateCreateAccess(CreateExportRequest(resourceType: null, requestType: ExportJobType.Group)));
        }

        [Fact]
        public void GivenGroupRouteWithSystemGroupAndPatientAccess_WhenCreatingExportWithoutType_ThenEffectiveTypeIsInferred()
        {
            ExportSmartScopeValidator validator = CreateValidator(
                new ScopeRestriction(KnownResourceTypes.Group, V1ExportRead, "system"),
                new ScopeRestriction(KnownResourceTypes.Patient, V1ExportRead, "system"));

            string effectiveResourceType = validator.ValidateCreateAccess(
                CreateExportRequest(resourceType: null, requestType: ExportJobType.Group));

            Assert.Equal("Group,Patient", effectiveResourceType);
        }

        [Fact]
        public void GivenGroupRouteWithSystemWildcardScope_WhenCreatingExportWithoutType_ThenAccessIsAllowed()
        {
            // system/* with complete export-read actions satisfies every route prerequisite.
            ExportSmartScopeValidator validator = CreateValidator(
                new ScopeRestriction(KnownResourceTypes.All, V2ExportRead, "system"));

            string effectiveResourceType = validator.ValidateCreateAccess(
                CreateExportRequest(resourceType: null, requestType: ExportJobType.Group));

            Assert.Null(effectiveResourceType);
        }

        [Fact]
        public void GivenPatientRouteWithSystemWildcardScope_WhenCreatingExportWithExplicitType_ThenAccessIsAllowed()
        {
            ExportSmartScopeValidator validator = CreateValidator(
                new ScopeRestriction(KnownResourceTypes.All, V1ExportRead, "system"));

            string effectiveResourceType = validator.ValidateCreateAccess(
                CreateExportRequest(KnownResourceTypes.Observation, ExportJobType.Patient));

            Assert.Equal(KnownResourceTypes.Observation, effectiveResourceType);
        }

        [Theory]
        [InlineData("patient")]
        [InlineData("user")]
        public void GivenPatientOrUserScope_WhenValidatingJobAccess_ThenForbiddenIsThrown(string scopeContext)
        {
            ExportSmartScopeValidator validator = CreateValidator(
                new ScopeRestriction(KnownResourceTypes.Patient, V1ExportRead, scopeContext));

            Assert.Throws<UnauthorizedFhirActionException>(() =>
                validator.ValidateJobAccess(CreateExportJobRecord(KnownResourceTypes.Patient)));
        }

        [Fact]
        public void GivenMatchingSystemScope_WhenValidatingExplicitTypeJob_ThenAccessIsAllowed()
        {
            ExportSmartScopeValidator validator = CreateValidator(
                new ScopeRestriction(KnownResourceTypes.Patient, V1ExportRead, "system"));

            validator.ValidateJobAccess(CreateExportJobRecord(KnownResourceTypes.Patient));
        }

        [Fact]
        public void GivenPersistedResourceTypeCaseDiffersFromSystemScope_WhenValidatingJobAccess_ThenAccessIsAllowed()
        {
            ExportSmartScopeValidator validator = CreateValidator(
                new ScopeRestriction("patient", V1ExportRead, "system"));

            validator.ValidateJobAccess(CreateExportJobRecord("PATIENT"));
        }

        [Fact]
        public void GivenMismatchedSystemScope_WhenValidatingExplicitTypeJob_ThenForbiddenIsThrown()
        {
            ExportSmartScopeValidator validator = CreateValidator(
                new ScopeRestriction(KnownResourceTypes.Observation, V1ExportRead, "system"));

            Assert.Throws<UnauthorizedFhirActionException>(() =>
                validator.ValidateJobAccess(CreateExportJobRecord(KnownResourceTypes.Patient)));
        }

        [Fact]
        public void GivenSystemScopeMissingCompletedOutputType_WhenValidatingExplicitTypeJob_ThenForbiddenIsThrown()
        {
            // Completed output types defensively tighten access if they extend beyond the persisted _type list.
            ExportSmartScopeValidator validator = CreateValidator(
                new ScopeRestriction(KnownResourceTypes.Patient, V1ExportRead, "system"));
            ExportJobRecord record = CreateExportJobRecord(KnownResourceTypes.Patient);
            record.Output.Add(KnownResourceTypes.Observation, new List<ExportFileInfo>());

            Assert.Throws<UnauthorizedFhirActionException>(() => validator.ValidateJobAccess(record));
        }

        [Fact]
        public void GivenOutputResourceTypeCaseDiffersFromSystemScope_WhenValidatingJobAccess_ThenAccessIsAllowed()
        {
            ExportSmartScopeValidator validator = CreateValidator(
                new ScopeRestriction(KnownResourceTypes.Patient, V1ExportRead, "system"),
                new ScopeRestriction("observation", V2ExportRead, "system"));
            ExportJobRecord record = CreateExportJobRecord(KnownResourceTypes.Patient);
            record.Output.Add("OBSERVATION", new List<ExportFileInfo>());

            validator.ValidateJobAccess(record);
        }

        [Fact]
        public void GivenPartialSystemScopeAndLegacyJobWithPartialOutput_WhenValidatingJobAccess_ThenForbiddenIsThrown()
        {
            // Missing persisted _type always means all resources; partial output must not narrow that requirement.
            ExportSmartScopeValidator validator = CreateValidator(
                new ScopeRestriction(KnownResourceTypes.Patient, V1ExportRead, "system"));
            ExportJobRecord record = CreateExportJobRecord(resourceType: null);
            record.Output.Add(KnownResourceTypes.Patient, new List<ExportFileInfo>());

            Assert.Throws<UnauthorizedFhirActionException>(() => validator.ValidateJobAccess(record));
        }

        [Fact]
        public void GivenSystemWildcardScopeAndLegacyJob_WhenValidatingJobAccess_ThenAccessIsAllowed()
        {
            ExportSmartScopeValidator validator = CreateValidator(
                new ScopeRestriction(KnownResourceTypes.All, V2ExportRead, "system"));
            ExportJobRecord record = CreateExportJobRecord(resourceType: null);
            record.Output.Add(KnownResourceTypes.Patient, new List<ExportFileInfo>());

            validator.ValidateJobAccess(record);
        }

        [Fact]
        public void GivenPersistedInferredResourceType_WhenValidatingJobAccess_ThenAccessIsAllowed()
        {
            // A job created without an explicit _type but narrowed via inference persists a comma-separated ResourceType.
            ExportSmartScopeValidator validator = CreateValidator(
                new ScopeRestriction(KnownResourceTypes.Patient, V1ExportRead, "system"),
                new ScopeRestriction(KnownResourceTypes.Observation, V2ExportRead, "system"));
            ExportJobRecord record = CreateExportJobRecord("Observation,Patient");

            validator.ValidateJobAccess(record);
        }

        [Fact]
        public void GivenGroupJobWithOnlySystemPatientAccess_WhenValidatingJobAccess_ThenForbiddenIsThrown()
        {
            // Route prerequisites must be re-derived from the persisted ExportType at status/cancel time too.
            ExportSmartScopeValidator validator = CreateValidator(
                new ScopeRestriction(KnownResourceTypes.Patient, V1ExportRead, "system"));
            ExportJobRecord record = CreateExportJobRecord(KnownResourceTypes.Patient, ExportJobType.Group);

            Assert.Throws<UnauthorizedFhirActionException>(() => validator.ValidateJobAccess(record));
        }

        [Fact]
        public void GivenGroupJobWithSystemGroupAndPatientAccess_WhenValidatingJobAccess_ThenAccessIsAllowed()
        {
            ExportSmartScopeValidator validator = CreateValidator(
                new ScopeRestriction(KnownResourceTypes.Group, V1ExportRead, "system"),
                new ScopeRestriction(KnownResourceTypes.Patient, V1ExportRead, "system"));
            ExportJobRecord record = CreateExportJobRecord(KnownResourceTypes.Patient, ExportJobType.Group);

            validator.ValidateJobAccess(record);
        }

        [Fact]
        public void GivenPatientJobWithoutSystemPatientAccess_WhenValidatingJobAccess_ThenForbiddenIsThrown()
        {
            ExportSmartScopeValidator validator = CreateValidator(
                new ScopeRestriction(KnownResourceTypes.Observation, V1ExportRead, "system"));
            ExportJobRecord record = CreateExportJobRecord(KnownResourceTypes.Observation, ExportJobType.Patient);

            Assert.Throws<UnauthorizedFhirActionException>(() => validator.ValidateJobAccess(record));
        }

        [Fact]
        public void GivenNonSmartRequest_WhenValidatingCreateOrJobAccess_ThenValidationIsNotApplied()
        {
            // SMART validation is gated by the explicit fine-grained flag, preserving existing RBAC-only behavior.
            ExportSmartScopeValidator validator = CreateValidator(
                applyFineGrainedAccessControl: false,
                new ScopeRestriction(KnownResourceTypes.Patient, V1ExportRead, "patient"));

            string effectiveResourceType = validator.ValidateCreateAccess(CreateExportRequest(resourceType: null));
            validator.ValidateJobAccess(CreateExportJobRecord(resourceType: null));

            Assert.Null(effectiveResourceType);
        }

        private ExportSmartScopeValidator CreateValidator(params ScopeRestriction[] scopeRestrictions)
        {
            return CreateValidator(applyFineGrainedAccessControl: true, scopeRestrictions);
        }

        private ExportSmartScopeValidator CreateValidator(
            bool applyFineGrainedAccessControl,
            params ScopeRestriction[] scopeRestrictions)
        {
            var requestContext = new FhirRequestContext(
                method: "GET",
                uriString: "http://localhost/",
                baseUriString: "http://localhost/",
                correlationId: "export-smart-scope-test",
                requestHeaders: new Dictionary<string, StringValues>(),
                responseHeaders: new Dictionary<string, StringValues>());

            requestContext.AccessControlContext.ApplyFineGrainedAccessControl = applyFineGrainedAccessControl;
            foreach (ScopeRestriction scopeRestriction in scopeRestrictions)
            {
                requestContext.AccessControlContext.AllowedResourceActions.Add(scopeRestriction);
            }

            _contextAccessor.RequestContext = requestContext;

            return new ExportSmartScopeValidator(_contextAccessor);
        }

        private static CreateExportRequest CreateExportRequest(string resourceType, ExportJobType requestType = ExportJobType.All)
        {
            return new CreateExportRequest(
                requestUri: new Uri("http://localhost/$export"),
                requestType: requestType,
                resourceType: resourceType);
        }

        private static ExportJobRecord CreateExportJobRecord(string resourceType, ExportJobType exportType = ExportJobType.All)
        {
            return new ExportJobRecord(
                requestUri: new Uri("http://localhost/$export"),
                exportType: exportType,
                exportFormat: ExportFormatTags.ResourceName,
                resourceType: resourceType,
                filters: Array.Empty<ExportJobFilter>(),
                hash: "hash",
                rollingFileSizeInMB: 64,
                smartRequest: true);
        }
    }
}
