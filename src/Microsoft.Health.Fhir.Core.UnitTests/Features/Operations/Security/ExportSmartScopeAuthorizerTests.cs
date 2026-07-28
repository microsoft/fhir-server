// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Hl7.Fhir.Rest;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Configs;
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
    public class ExportSmartScopeAuthorizerTests
    {
        private static readonly DataActions V1ExportRead = DataActions.Read | DataActions.Export;
        private static readonly DataActions V2ExportRead = DataActions.ReadById | DataActions.Search | DataActions.Export;

        private readonly RequestContextAccessor<IFhirRequestContext> _contextAccessor = new FhirRequestContextAccessor();

        [Fact]
        public void GivenDefaultCoreFeatureConfiguration_WhenCheckingSmartExportScopeAuthorization_ThenItIsEnabled()
        {
            Assert.True(new CoreFeatureConfiguration().EnableSmartExportScopeAuthorization);
        }

        [Fact]
        public void GivenMissingRequestContext_WhenAuthorizingExportAccess_ThenRequestedResourceTypeIsReturnedAndJobAccessIsAllowed()
        {
            _contextAccessor.RequestContext = null;
            var authorizer = new ExportSmartScopeAuthorizer(
                _contextAccessor,
                Options.Create(new CoreFeatureConfiguration()));
            CreateExportRequest request = CreateExportRequest(KnownResourceTypes.Patient);

            string resourceType = authorizer.AuthorizeCreateAndResolveResourceType(request);

            Assert.Equal(request.ResourceType, resourceType);
            authorizer.AuthorizeJobAccess(CreateExportJobRecord(KnownResourceTypes.Patient));
        }

        [Fact]
        public void GivenNonSmartRequest_WhenAuthorizingExportAccess_ThenRequestedResourceTypeIsReturnedAndJobAccessIsAllowed()
        {
            ExportSmartScopeAuthorizer authorizer = CreateAuthorizer(
                enableSmartExportScopeAuthorization: true,
                isSmartRequest: false);
            CreateExportRequest request = CreateExportRequest(KnownResourceTypes.Patient);

            string resourceType = authorizer.AuthorizeCreateAndResolveResourceType(request);

            Assert.Equal(request.ResourceType, resourceType);
            authorizer.AuthorizeJobAccess(CreateExportJobRecord(KnownResourceTypes.Patient));
        }

        [Theory]
        [InlineData("patient")]
        [InlineData("user")]
        public void GivenPatientOrUserScope_WhenValidatingCreateOrJobAccess_ThenForbiddenIsThrown(string scopeContext)
        {
            ExportSmartScopeAuthorizer validator = CreateAuthorizer(
                new ScopeRestriction(KnownResourceTypes.Patient, V1ExportRead, scopeContext));

            Assert.Throws<UnauthorizedFhirActionException>(() =>
                validator.AuthorizeCreateAndResolveResourceType(CreateExportRequest(KnownResourceTypes.Patient)));

            Assert.Throws<UnauthorizedFhirActionException>(() =>
                validator.AuthorizeJobAccess(CreateExportJobRecord(KnownResourceTypes.Patient)));
        }

        [Fact]
        public void GivenUppercaseSystemScope_WhenValidatingCreateOrJobAccess_ThenForbiddenIsThrown()
        {
            ExportSmartScopeAuthorizer validator = CreateAuthorizer(
                new ScopeRestriction(KnownResourceTypes.Patient, V1ExportRead, "SYSTEM"));

            Assert.Throws<UnauthorizedFhirActionException>(() =>
                validator.AuthorizeCreateAndResolveResourceType(CreateExportRequest(KnownResourceTypes.Patient)));

            Assert.Throws<UnauthorizedFhirActionException>(() =>
                validator.AuthorizeJobAccess(CreateExportJobRecord(KnownResourceTypes.Patient)));
        }

        [Fact]
        public void GivenFineGrainedContextWithoutScopeRestrictions_WhenValidatingExportAccess_ThenForbiddenIsThrown()
        {
            // Fine-grained access can be enabled before scopes are parsed, so an empty result must fail closed.
            ExportSmartScopeAuthorizer validator = CreateAuthorizer();

            Assert.Throws<UnauthorizedFhirActionException>(() =>
                validator.AuthorizeCreateAndResolveResourceType(CreateExportRequest(KnownResourceTypes.Patient)));
            Assert.Throws<UnauthorizedFhirActionException>(() =>
                validator.AuthorizeJobAccess(CreateExportJobRecord(KnownResourceTypes.Patient)));

            // Without any explicit _type, an empty scope set has no eligible resource types to infer either.
            Assert.Throws<UnauthorizedFhirActionException>(() =>
                validator.AuthorizeCreateAndResolveResourceType(CreateExportRequest(resourceType: null)));
        }

        [Fact]
        public void GivenMixedPatientAndSystemScopes_WhenValidatingExportAccess_ThenOnlySystemScopeAuthorizesTypes()
        {
            // Patient or user scopes may coexist with system scopes but must never broaden export access.
            ExportSmartScopeAuthorizer validator = CreateAuthorizer(
                new ScopeRestriction(KnownResourceTypes.Observation, V1ExportRead, "patient"),
                new ScopeRestriction(KnownResourceTypes.Patient, V1ExportRead, "system"));

            validator.AuthorizeCreateAndResolveResourceType(CreateExportRequest(KnownResourceTypes.Patient));
            Assert.Throws<UnauthorizedFhirActionException>(() =>
                validator.AuthorizeCreateAndResolveResourceType(CreateExportRequest(KnownResourceTypes.Observation)));
        }

        [Theory]
        [InlineData(DataActions.Read | DataActions.Export)]
        [InlineData(DataActions.ReadById | DataActions.Search | DataActions.Export)]
        public void GivenSystemWildcardExportReadScope_WhenCreatingExportWithoutType_ThenAccessIsAllowedAndTypeStaysUnconstrained(DataActions actions)
        {
            ExportSmartScopeAuthorizer validator = CreateAuthorizer(
                new ScopeRestriction(KnownResourceTypes.All, actions, "system"));

            string effectiveResourceType = validator.AuthorizeCreateAndResolveResourceType(CreateExportRequest(resourceType: null));

            Assert.Null(effectiveResourceType);
        }

        [Fact]
        public void GivenSinglePartialSystemScope_WhenCreatingExportWithoutType_ThenEffectiveTypeIsInferredAndNarrowed()
        {
            // A partial system scope with complete export-read actions now narrows the export instead of being forbidden.
            ExportSmartScopeAuthorizer validator = CreateAuthorizer(
                new ScopeRestriction(KnownResourceTypes.Patient, V1ExportRead, "system"));

            string effectiveResourceType = validator.AuthorizeCreateAndResolveResourceType(CreateExportRequest(resourceType: null));

            Assert.Equal(KnownResourceTypes.Patient, effectiveResourceType);
        }

        [Fact]
        public void GivenMultiplePartialSystemScopes_WhenCreatingExportWithoutType_ThenEffectiveTypeIncludesEveryEligibleType()
        {
            ExportSmartScopeAuthorizer validator = CreateAuthorizer(
                new ScopeRestriction(KnownResourceTypes.Patient, V1ExportRead, "system"),
                new ScopeRestriction(KnownResourceTypes.Observation, V2ExportRead, "system"));

            string effectiveResourceType = validator.AuthorizeCreateAndResolveResourceType(CreateExportRequest(resourceType: null));

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
            ExportSmartScopeAuthorizer validator = CreateAuthorizer(
                new ScopeRestriction(KnownResourceTypes.Patient, actions, "system"));

            Assert.Throws<UnauthorizedFhirActionException>(() =>
                validator.AuthorizeCreateAndResolveResourceType(CreateExportRequest(resourceType: null)));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" , ")]
        public void GivenPartialSystemScope_WhenCreatingExportWithEffectivelyEmptyType_ThenEffectiveTypeIsInferred(string resourceType)
        {
            ExportSmartScopeAuthorizer validator = CreateAuthorizer(
                new ScopeRestriction(KnownResourceTypes.Patient, V1ExportRead, "system"));

            string effectiveResourceType = validator.AuthorizeCreateAndResolveResourceType(CreateExportRequest(resourceType));

            Assert.Equal(KnownResourceTypes.Patient, effectiveResourceType);
        }

        [Theory]
        [InlineData(DataActions.Read | DataActions.Export)]
        [InlineData(DataActions.ReadById | DataActions.Search | DataActions.Export)]
        public void GivenMatchingSystemExportReadScope_WhenCreatingExportWithExplicitType_ThenAccessIsAllowedAndTypeIsPreserved(DataActions actions)
        {
            ExportSmartScopeAuthorizer validator = CreateAuthorizer(
                new ScopeRestriction(KnownResourceTypes.Patient, actions, "system"));

            string effectiveResourceType = validator.AuthorizeCreateAndResolveResourceType(CreateExportRequest(KnownResourceTypes.Patient));

            Assert.Equal(KnownResourceTypes.Patient, effectiveResourceType);
        }

        [Fact]
        public void GivenResourceTypeCaseDiffersFromSystemScope_WhenCreatingExport_ThenForbiddenIsThrown()
        {
            ExportSmartScopeAuthorizer validator = CreateAuthorizer(
                new ScopeRestriction("patient", V1ExportRead, "system"));

            Assert.Throws<UnauthorizedFhirActionException>(() =>
                validator.AuthorizeCreateAndResolveResourceType(CreateExportRequest(KnownResourceTypes.Patient)));
        }

        [Fact]
        public void GivenSystemScopesCoveringEveryRequestedType_WhenCreatingExportWithExplicitTypes_ThenAccessIsAllowedAndSubsetIsPreserved()
        {
            ExportSmartScopeAuthorizer validator = CreateAuthorizer(
                new ScopeRestriction(KnownResourceTypes.Patient, V1ExportRead, "system"),
                new ScopeRestriction(KnownResourceTypes.Observation, V2ExportRead, "system"));

            string effectiveResourceType = validator.AuthorizeCreateAndResolveResourceType(CreateExportRequest("Patient,Observation"));

            Assert.Equal("Patient,Observation", effectiveResourceType);
        }

        [Fact]
        public void GivenSystemScopeMissingRequestedType_WhenCreatingExportWithExplicitTypes_ThenForbiddenIsThrown()
        {
            ExportSmartScopeAuthorizer validator = CreateAuthorizer(
                new ScopeRestriction(KnownResourceTypes.Patient, V1ExportRead, "system"));

            Assert.Throws<UnauthorizedFhirActionException>(() =>
                validator.AuthorizeCreateAndResolveResourceType(CreateExportRequest("Patient,Observation")));
        }

        [Theory]
        [InlineData(DataActions.Export)]
        [InlineData(DataActions.Read)]
        [InlineData(DataActions.ReadById | DataActions.Export)]
        [InlineData(DataActions.Search | DataActions.Export)]
        public void GivenIncompleteSystemScopeActions_WhenCreatingExport_ThenForbiddenIsThrown(DataActions actions)
        {
            ExportSmartScopeAuthorizer validator = CreateAuthorizer(
                new ScopeRestriction(KnownResourceTypes.Patient, actions, "system"));

            Assert.Throws<UnauthorizedFhirActionException>(() =>
                validator.AuthorizeCreateAndResolveResourceType(CreateExportRequest(KnownResourceTypes.Patient)));
        }

        [Theory]
        [InlineData(KnownResourceTypes.Patient)]
        [InlineData(null)]
        public void GivenSearchParameterConstrainedSystemScope_WhenCreatingExport_ThenForbiddenIsThrown(string resourceType)
        {
            // Bulk export cannot safely enforce scope search constraints, so constrained scopes neither
            // authorize an explicit type nor contribute a type eligible for inference.
            ExportSmartScopeAuthorizer validator = CreateAuthorizer(
                new ScopeRestriction(
                    KnownResourceTypes.Patient,
                    V2ExportRead,
                    "system",
                    new SearchParams("active", "true")));

            Assert.Throws<UnauthorizedFhirActionException>(() =>
                validator.AuthorizeCreateAndResolveResourceType(CreateExportRequest(resourceType)));
        }

        [Fact]
        public void GivenPatientRouteWithoutSystemPatientAccess_WhenCreatingExportWithExplicitType_ThenForbiddenIsThrown()
        {
            // Patient/$export?_type=Observation must require system/Patient in addition to the explicit output type.
            ExportSmartScopeAuthorizer validator = CreateAuthorizer(
                new ScopeRestriction(KnownResourceTypes.Observation, V1ExportRead, "system"));

            Assert.Throws<UnauthorizedFhirActionException>(() =>
                validator.AuthorizeCreateAndResolveResourceType(CreateExportRequest(KnownResourceTypes.Observation, ExportJobType.Patient)));
        }

        [Fact]
        public void GivenPatientRouteWithSystemPatientAndOutputTypeAccess_WhenCreatingExportWithExplicitType_ThenAccessIsAllowed()
        {
            ExportSmartScopeAuthorizer validator = CreateAuthorizer(
                new ScopeRestriction(KnownResourceTypes.Observation, V1ExportRead, "system"),
                new ScopeRestriction(KnownResourceTypes.Patient, V1ExportRead, "system"));

            string effectiveResourceType = validator.AuthorizeCreateAndResolveResourceType(
                CreateExportRequest(KnownResourceTypes.Observation, ExportJobType.Patient));

            Assert.Equal(KnownResourceTypes.Observation, effectiveResourceType);
        }

        [Theory]
        [InlineData(KnownResourceTypes.Patient)]
        [InlineData(null)]
        public void GivenGroupRouteWithOnlySystemPatientAccess_WhenCreatingExport_ThenForbiddenIsThrown(string resourceType)
        {
            // Group/{id}/$export must require both system/Group and system/Patient for explicit and inferred types.
            ExportSmartScopeAuthorizer validator = CreateAuthorizer(
                new ScopeRestriction(KnownResourceTypes.Patient, V1ExportRead, "system"));

            Assert.Throws<UnauthorizedFhirActionException>(() =>
                validator.AuthorizeCreateAndResolveResourceType(CreateExportRequest(resourceType, ExportJobType.Group)));
        }

        [Theory]
        [InlineData(KnownResourceTypes.Patient, KnownResourceTypes.Patient)]
        [InlineData(null, "Group,Patient")]
        public void GivenGroupRouteWithSystemGroupAndPatientAccess_WhenCreatingExport_ThenAccessIsAllowed(
            string resourceType,
            string expectedResourceType)
        {
            ExportSmartScopeAuthorizer validator = CreateAuthorizer(
                new ScopeRestriction(KnownResourceTypes.Group, V1ExportRead, "system"),
                new ScopeRestriction(KnownResourceTypes.Patient, V1ExportRead, "system"));

            string effectiveResourceType = validator.AuthorizeCreateAndResolveResourceType(
                CreateExportRequest(resourceType, ExportJobType.Group));

            Assert.Equal(expectedResourceType, effectiveResourceType);
        }

        [Fact]
        public void GivenGroupRouteWithSystemWildcardScope_WhenCreatingExportWithoutType_ThenAccessIsAllowed()
        {
            // system/* with complete export-read actions satisfies every route prerequisite.
            ExportSmartScopeAuthorizer validator = CreateAuthorizer(
                new ScopeRestriction(KnownResourceTypes.All, V2ExportRead, "system"));

            string effectiveResourceType = validator.AuthorizeCreateAndResolveResourceType(
                CreateExportRequest(resourceType: null, requestType: ExportJobType.Group));

            Assert.Null(effectiveResourceType);
        }

        [Fact]
        public void GivenPatientRouteWithSystemWildcardScope_WhenCreatingExportWithExplicitType_ThenAccessIsAllowed()
        {
            ExportSmartScopeAuthorizer validator = CreateAuthorizer(
                new ScopeRestriction(KnownResourceTypes.All, V1ExportRead, "system"));

            string effectiveResourceType = validator.AuthorizeCreateAndResolveResourceType(
                CreateExportRequest(KnownResourceTypes.Observation, ExportJobType.Patient));

            Assert.Equal(KnownResourceTypes.Observation, effectiveResourceType);
        }

        [Fact]
        public void GivenMatchingSystemScope_WhenValidatingExplicitTypeJob_ThenAccessIsAllowed()
        {
            ExportSmartScopeAuthorizer validator = CreateAuthorizer(
                new ScopeRestriction(KnownResourceTypes.Patient, V1ExportRead, "system"));

            validator.AuthorizeJobAccess(CreateExportJobRecord(KnownResourceTypes.Patient));
        }

        [Fact]
        public void GivenPersistedResourceTypeCaseDiffersFromSystemScope_WhenValidatingJobAccess_ThenForbiddenIsThrown()
        {
            ExportSmartScopeAuthorizer validator = CreateAuthorizer(
                new ScopeRestriction("patient", V1ExportRead, "system"));

            Assert.Throws<UnauthorizedFhirActionException>(() =>
                validator.AuthorizeJobAccess(CreateExportJobRecord(KnownResourceTypes.Patient)));
        }

        [Fact]
        public void GivenMismatchedSystemScope_WhenValidatingExplicitTypeJob_ThenForbiddenIsThrown()
        {
            ExportSmartScopeAuthorizer validator = CreateAuthorizer(
                new ScopeRestriction(KnownResourceTypes.Observation, V1ExportRead, "system"));

            Assert.Throws<UnauthorizedFhirActionException>(() =>
                validator.AuthorizeJobAccess(CreateExportJobRecord(KnownResourceTypes.Patient)));
        }

        [Fact]
        public void GivenSystemScopeMissingCompletedOutputType_WhenValidatingExplicitTypeJob_ThenForbiddenIsThrown()
        {
            // Completed output types defensively tighten access if they extend beyond the persisted _type list.
            ExportSmartScopeAuthorizer validator = CreateAuthorizer(
                new ScopeRestriction(KnownResourceTypes.Patient, V1ExportRead, "system"));
            ExportJobRecord record = CreateExportJobRecord(KnownResourceTypes.Patient);
            record.Output.Add(KnownResourceTypes.Observation, new List<ExportFileInfo>());

            Assert.Throws<UnauthorizedFhirActionException>(() => validator.AuthorizeJobAccess(record));
        }

        [Fact]
        public void GivenOutputResourceTypeCaseDiffersFromSystemScope_WhenValidatingJobAccess_ThenForbiddenIsThrown()
        {
            ExportSmartScopeAuthorizer validator = CreateAuthorizer(
                new ScopeRestriction(KnownResourceTypes.Patient, V1ExportRead, "system"),
                new ScopeRestriction("observation", V2ExportRead, "system"));
            ExportJobRecord record = CreateExportJobRecord(KnownResourceTypes.Patient);
            record.Output.Add(KnownResourceTypes.Observation, new List<ExportFileInfo>());

            Assert.Throws<UnauthorizedFhirActionException>(() => validator.AuthorizeJobAccess(record));
        }

        [Fact]
        public void GivenPartialSystemScopeAndLegacyJobWithPartialOutput_WhenValidatingJobAccess_ThenForbiddenIsThrown()
        {
            // Missing persisted _type always means all resources; partial output must not narrow that requirement.
            ExportSmartScopeAuthorizer validator = CreateAuthorizer(
                new ScopeRestriction(KnownResourceTypes.Patient, V1ExportRead, "system"));
            ExportJobRecord record = CreateExportJobRecord(resourceType: null);
            record.Output.Add(KnownResourceTypes.Patient, new List<ExportFileInfo>());

            Assert.Throws<UnauthorizedFhirActionException>(() => validator.AuthorizeJobAccess(record));
        }

        [Fact]
        public void GivenSystemWildcardScopeAndLegacyJob_WhenValidatingJobAccess_ThenAccessIsAllowed()
        {
            ExportSmartScopeAuthorizer validator = CreateAuthorizer(
                new ScopeRestriction(KnownResourceTypes.All, V2ExportRead, "system"));
            ExportJobRecord record = CreateExportJobRecord(resourceType: null);
            record.Output.Add(KnownResourceTypes.Patient, new List<ExportFileInfo>());

            validator.AuthorizeJobAccess(record);
        }

        [Fact]
        public void GivenPersistedInferredResourceType_WhenValidatingJobAccess_ThenAccessIsAllowed()
        {
            // A job created without an explicit _type but narrowed via inference persists a comma-separated ResourceType.
            ExportSmartScopeAuthorizer validator = CreateAuthorizer(
                new ScopeRestriction(KnownResourceTypes.Patient, V1ExportRead, "system"),
                new ScopeRestriction(KnownResourceTypes.Observation, V2ExportRead, "system"));
            ExportJobRecord record = CreateExportJobRecord("Observation,Patient");

            validator.AuthorizeJobAccess(record);
        }

        [Fact]
        public void GivenGroupJobWithOnlySystemPatientAccess_WhenValidatingJobAccess_ThenForbiddenIsThrown()
        {
            // Route prerequisites must be re-derived from the persisted ExportType at status/cancel time too.
            ExportSmartScopeAuthorizer validator = CreateAuthorizer(
                new ScopeRestriction(KnownResourceTypes.Patient, V1ExportRead, "system"));
            ExportJobRecord record = CreateExportJobRecord(KnownResourceTypes.Patient, ExportJobType.Group);

            Assert.Throws<UnauthorizedFhirActionException>(() => validator.AuthorizeJobAccess(record));
        }

        [Fact]
        public void GivenGroupJobWithSystemGroupAndPatientAccess_WhenValidatingJobAccess_ThenAccessIsAllowed()
        {
            ExportSmartScopeAuthorizer validator = CreateAuthorizer(
                new ScopeRestriction(KnownResourceTypes.Group, V1ExportRead, "system"),
                new ScopeRestriction(KnownResourceTypes.Patient, V1ExportRead, "system"));
            ExportJobRecord record = CreateExportJobRecord(KnownResourceTypes.Patient, ExportJobType.Group);

            validator.AuthorizeJobAccess(record);
        }

        [Fact]
        public void GivenPatientJobWithoutSystemPatientAccess_WhenValidatingJobAccess_ThenForbiddenIsThrown()
        {
            ExportSmartScopeAuthorizer validator = CreateAuthorizer(
                new ScopeRestriction(KnownResourceTypes.Observation, V1ExportRead, "system"));
            ExportJobRecord record = CreateExportJobRecord(KnownResourceTypes.Observation, ExportJobType.Patient);

            Assert.Throws<UnauthorizedFhirActionException>(() => validator.AuthorizeJobAccess(record));
        }

        [Fact]
        public void GivenSmartExportScopeAuthorizationIsDisabled_WhenCreatingExportWithExplicitType_ThenRequestedTypeIsUnchanged()
        {
            ExportSmartScopeAuthorizer validator = CreateAuthorizer(enableSmartExportScopeAuthorization: false);
            const string requestedResourceType = "Patient, patient";

            string effectiveResourceType = validator.AuthorizeCreateAndResolveResourceType(
                CreateExportRequest(requestedResourceType));

            Assert.Equal(requestedResourceType, effectiveResourceType);
        }

        [Fact]
        public void GivenSmartExportScopeAuthorizationIsDisabled_WhenCreatingExportWithoutType_ThenResourceTypeRemainsNull()
        {
            ExportSmartScopeAuthorizer validator = CreateAuthorizer(enableSmartExportScopeAuthorization: false);

            string effectiveResourceType = validator.AuthorizeCreateAndResolveResourceType(
                CreateExportRequest(resourceType: null));

            Assert.Null(effectiveResourceType);
        }

        [Fact]
        public void GivenSmartExportScopeAuthorizationIsDisabled_WhenAccessingJob_ThenAccessIsAllowedWithoutScopes()
        {
            ExportSmartScopeAuthorizer validator = CreateAuthorizer(enableSmartExportScopeAuthorization: false);

            validator.AuthorizeJobAccess(CreateExportJobRecord(KnownResourceTypes.Patient));
        }

        private ExportSmartScopeAuthorizer CreateAuthorizer(params ScopeRestriction[] scopeRestrictions)
        {
            return CreateAuthorizer(enableSmartExportScopeAuthorization: true, isSmartRequest: true, scopeRestrictions);
        }

        private ExportSmartScopeAuthorizer CreateAuthorizer(
            bool enableSmartExportScopeAuthorization,
            params ScopeRestriction[] scopeRestrictions)
        {
            return CreateAuthorizer(enableSmartExportScopeAuthorization, isSmartRequest: true, scopeRestrictions);
        }

        private ExportSmartScopeAuthorizer CreateAuthorizer(
            bool enableSmartExportScopeAuthorization,
            bool isSmartRequest,
            params ScopeRestriction[] scopeRestrictions)
        {
            var requestContext = new FhirRequestContext(
                method: "GET",
                uriString: "http://localhost/",
                baseUriString: "http://localhost/",
                correlationId: "export-smart-scope-test",
                requestHeaders: new Dictionary<string, StringValues>(),
                responseHeaders: new Dictionary<string, StringValues>());

            requestContext.AccessControlContext.ApplyFineGrainedAccessControl = isSmartRequest;
            foreach (ScopeRestriction scopeRestriction in scopeRestrictions)
            {
                requestContext.AccessControlContext.AllowedResourceActions.Add(scopeRestriction);
            }

            _contextAccessor.RequestContext = requestContext;

            return new ExportSmartScopeAuthorizer(
                _contextAccessor,
                Options.Create(new CoreFeatureConfiguration
                {
                    EnableSmartExportScopeAuthorization = enableSmartExportScopeAuthorization,
                }));
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
