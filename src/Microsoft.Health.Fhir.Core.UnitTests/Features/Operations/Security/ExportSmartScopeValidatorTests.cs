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
        public void GivenSystemWildcardExportReadScope_WhenCreatingExportWithoutType_ThenAccessIsAllowed(DataActions actions)
        {
            ExportSmartScopeValidator validator = CreateValidator(
                new ScopeRestriction(KnownResourceTypes.All, actions, "system"));

            validator.ValidateCreateAccess(CreateExportRequest(resourceType: null));
        }

        [Fact]
        public void GivenPartialSystemScope_WhenCreatingExportWithoutType_ThenForbiddenIsThrown()
        {
            ExportSmartScopeValidator validator = CreateValidator(
                new ScopeRestriction(KnownResourceTypes.Patient, V1ExportRead, "system"));

            Assert.Throws<UnauthorizedFhirActionException>(() =>
                validator.ValidateCreateAccess(CreateExportRequest(resourceType: null)));
        }

        [Theory]
        [InlineData(DataActions.Read | DataActions.Export)]
        [InlineData(DataActions.ReadById | DataActions.Search | DataActions.Export)]
        public void GivenMatchingSystemExportReadScope_WhenCreatingExportWithExplicitType_ThenAccessIsAllowed(DataActions actions)
        {
            ExportSmartScopeValidator validator = CreateValidator(
                new ScopeRestriction(KnownResourceTypes.Patient, actions, "system"));

            validator.ValidateCreateAccess(CreateExportRequest(KnownResourceTypes.Patient));
        }

        [Fact]
        public void GivenResourceTypeCaseDiffersFromSystemScope_WhenCreatingExport_ThenAccessIsAllowed()
        {
            ExportSmartScopeValidator validator = CreateValidator(
                new ScopeRestriction("patient", V1ExportRead, "system"));

            validator.ValidateCreateAccess(CreateExportRequest("PATIENT"));
        }

        [Fact]
        public void GivenSystemScopesCoveringEveryRequestedType_WhenCreatingExportWithExplicitTypes_ThenAccessIsAllowed()
        {
            ExportSmartScopeValidator validator = CreateValidator(
                new ScopeRestriction(KnownResourceTypes.Patient, V1ExportRead, "system"),
                new ScopeRestriction(KnownResourceTypes.Observation, V2ExportRead, "system"));

            validator.ValidateCreateAccess(CreateExportRequest("Patient,Observation"));
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

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" , ")]
        public void GivenPartialSystemScope_WhenCreatingExportWithoutNonemptyExplicitType_ThenForbiddenIsThrown(string resourceType)
        {
            ExportSmartScopeValidator validator = CreateValidator(
                new ScopeRestriction(KnownResourceTypes.Patient, V1ExportRead, "system"));

            Assert.Throws<UnauthorizedFhirActionException>(() =>
                validator.ValidateCreateAccess(CreateExportRequest(resourceType)));
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
        public void GivenNonSmartRequest_WhenValidatingCreateOrJobAccess_ThenValidationIsNotApplied()
        {
            // SMART validation is gated by the explicit fine-grained flag, preserving existing RBAC-only behavior.
            ExportSmartScopeValidator validator = CreateValidator(
                applyFineGrainedAccessControl: false,
                new ScopeRestriction(KnownResourceTypes.Patient, V1ExportRead, "patient"));

            validator.ValidateCreateAccess(CreateExportRequest(resourceType: null));
            validator.ValidateJobAccess(CreateExportJobRecord(resourceType: null));
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

        private static CreateExportRequest CreateExportRequest(string resourceType)
        {
            return new CreateExportRequest(
                requestUri: new Uri("http://localhost/$export"),
                requestType: ExportJobType.All,
                resourceType: resourceType);
        }

        private static ExportJobRecord CreateExportJobRecord(string resourceType)
        {
            return new ExportJobRecord(
                requestUri: new Uri("http://localhost/$export"),
                exportType: ExportJobType.All,
                exportFormat: ExportFormatTags.ResourceName,
                resourceType: resourceType,
                filters: Array.Empty<ExportJobFilter>(),
                hash: "hash",
                rollingFileSizeInMB: 64,
                smartRequest: true);
        }
    }
}
