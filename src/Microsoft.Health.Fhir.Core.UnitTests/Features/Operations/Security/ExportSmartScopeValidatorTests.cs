// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Operations.Security;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Messages.Export;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.Security
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Security)]
    public class ExportSmartScopeValidatorTests
    {
        private static readonly DataActions V1ReadActions = DataActions.Read | DataActions.Export | DataActions.Search;
        private static readonly DataActions V2ReadSearchActions = DataActions.ReadById | DataActions.Search | DataActions.Export;

        private readonly RequestContextAccessor<IFhirRequestContext> _contextAccessor;
        private readonly ISearchService _searchService;

        public ExportSmartScopeValidatorTests()
        {
            _contextAccessor = new FhirRequestContextAccessor();
            _searchService = Substitute.For<ISearchService>();
        }

        [Fact]
        public async Task GivenPatientReadScopeForSamePatientAndExplicitType_WhenCreatingPatientInstanceExport_ThenAccessIsAllowed()
        {
            ExportSmartScopeValidator validator = CreateValidator(
                compartmentResourceType: KnownResourceTypes.Patient,
                compartmentId: "123",
                new ScopeRestriction(KnownResourceTypes.Patient, V1ReadActions, "patient"));

            bool bindToSmartCompartment = await validator.ValidateCreateAccessAsync(
                CreatePatientExportRequest("123", KnownResourceTypes.Patient),
                CancellationToken.None);

            Assert.True(bindToSmartCompartment);
        }

        [Fact]
        public async Task GivenPatientReadScopeWithoutExplicitType_WhenCreatingPatientInstanceExport_ThenForbiddenIsThrown()
        {
            ExportSmartScopeValidator validator = CreateValidator(
                compartmentResourceType: KnownResourceTypes.Patient,
                compartmentId: "123",
                new ScopeRestriction(KnownResourceTypes.Patient, V1ReadActions, "patient"));

            await Assert.ThrowsAsync<UnauthorizedFhirActionException>(() =>
                validator.ValidateCreateAccessAsync(
                    CreatePatientExportRequest("123", resourceType: null),
                    CancellationToken.None));
        }

        [Fact]
        public async Task GivenPatientReadScopeForDifferentPatient_WhenCreatingPatientInstanceExport_ThenForbiddenIsThrown()
        {
            ExportSmartScopeValidator validator = CreateValidator(
                compartmentResourceType: KnownResourceTypes.Patient,
                compartmentId: "123",
                new ScopeRestriction(KnownResourceTypes.Patient, V1ReadActions, "patient"));

            await Assert.ThrowsAsync<UnauthorizedFhirActionException>(() =>
                validator.ValidateCreateAccessAsync(
                    CreatePatientExportRequest("456", KnownResourceTypes.Patient),
                    CancellationToken.None));
        }

        [Fact]
        public async Task GivenUserReadScopeAndTargetPatientInPractitionerCompartment_WhenCreatingPatientInstanceExport_ThenAccessIsAllowed()
        {
            ExportSmartScopeValidator validator = CreateValidator(
                compartmentResourceType: KnownResourceTypes.Practitioner,
                compartmentId: "456",
                new ScopeRestriction(KnownResourceTypes.Patient, V1ReadActions, "user"));
            _searchService.SearchCompartmentAsync(
                KnownResourceTypes.Practitioner,
                "456",
                KnownResourceTypes.Patient,
                Arg.Is<IReadOnlyList<Tuple<string, string>>>(queries =>
                    queries.Contains(Tuple.Create(KnownQueryParameterNames.Id, "123"))
                    && queries.Contains(Tuple.Create(KnownQueryParameterNames.Count, "0"))),
                CancellationToken.None,
                false,
                true)
                .Returns(new SearchResult(totalCount: 1, unsupportedSearchParameters: Array.Empty<Tuple<string, string>>()));

            bool bindToSmartCompartment = await validator.ValidateCreateAccessAsync(
                CreatePatientExportRequest("123", KnownResourceTypes.Patient),
                CancellationToken.None);

            Assert.True(bindToSmartCompartment);
        }

        [Fact]
        public async Task GivenUserReadScopeAndTargetPatientOutsidePractitionerCompartment_WhenCreatingPatientInstanceExport_ThenForbiddenIsThrown()
        {
            ExportSmartScopeValidator validator = CreateValidator(
                compartmentResourceType: KnownResourceTypes.Practitioner,
                compartmentId: "456",
                new ScopeRestriction(KnownResourceTypes.Patient, V1ReadActions, "user"));
            _searchService.SearchCompartmentAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>(),
                Arg.Any<bool>())
                .Returns(SearchResult.Empty());

            await Assert.ThrowsAsync<UnauthorizedFhirActionException>(() =>
                validator.ValidateCreateAccessAsync(
                    CreatePatientExportRequest("123", KnownResourceTypes.Patient),
                    CancellationToken.None));
        }

        [Fact]
        public async Task GivenUserReadScopeAndMultipleTargetPatientIds_WhenCreatingPatientInstanceExport_ThenForbiddenIsThrown()
        {
            ExportSmartScopeValidator validator = CreateValidator(
                compartmentResourceType: KnownResourceTypes.Practitioner,
                compartmentId: "456",
                new ScopeRestriction(KnownResourceTypes.Patient, V1ReadActions, "user"));
            _searchService.SearchCompartmentAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>(),
                Arg.Any<bool>())
                .Returns(new SearchResult(totalCount: 1, unsupportedSearchParameters: Array.Empty<Tuple<string, string>>()));

            await Assert.ThrowsAsync<UnauthorizedFhirActionException>(() =>
                validator.ValidateCreateAccessAsync(
                    CreatePatientExportRequest("123,789", KnownResourceTypes.Patient),
                    CancellationToken.None));

            await _searchService.DidNotReceiveWithAnyArgs().SearchCompartmentAsync(
                default,
                default,
                default,
                default,
                default,
                default,
                default);
        }

        [Fact]
        public async Task GivenSystemReadScopeCoveringRequestedType_WhenCreatingPatientInstanceExport_ThenAccessIsAllowed()
        {
            ExportSmartScopeValidator validator = CreateValidator(
                compartmentResourceType: null,
                compartmentId: null,
                new ScopeRestriction(KnownResourceTypes.Patient, V2ReadSearchActions, "system"));

            bool bindToSmartCompartment = await validator.ValidateCreateAccessAsync(
                CreatePatientExportRequest("123", KnownResourceTypes.Patient),
                CancellationToken.None);

            Assert.False(bindToSmartCompartment);
        }

        [Fact]
        public async Task GivenSystemReadScopeMissingOneRequestedType_WhenCreatingPatientInstanceExport_ThenForbiddenIsThrown()
        {
            ExportSmartScopeValidator validator = CreateValidator(
                compartmentResourceType: null,
                compartmentId: null,
                new ScopeRestriction(KnownResourceTypes.Patient, V1ReadActions, "system"));

            await Assert.ThrowsAsync<UnauthorizedFhirActionException>(() =>
                validator.ValidateCreateAccessAsync(
                    CreatePatientExportRequest("123", "Patient,Observation"),
                    CancellationToken.None));
        }

        [Fact]
        public void GivenPatientReadScopeAndJobCreatedInSamePatientContext_WhenValidatingJobAccess_ThenAccessIsAllowed()
        {
            ExportSmartScopeValidator validator = CreateValidator(
                compartmentResourceType: KnownResourceTypes.Patient,
                compartmentId: "123",
                new ScopeRestriction(KnownResourceTypes.Patient, V1ReadActions, "patient"));
            ExportJobRecord record = CreateExportJobRecord(
                resourceType: KnownResourceTypes.Patient,
                smartCompartmentResourceType: KnownResourceTypes.Patient,
                smartCompartmentId: "123");

            validator.ValidateJobAccess(record);
        }

        [Fact]
        public void GivenPatientReadScopeAndJobCreatedInDifferentPatientContext_WhenValidatingJobAccess_ThenForbiddenIsThrown()
        {
            ExportSmartScopeValidator validator = CreateValidator(
                compartmentResourceType: KnownResourceTypes.Patient,
                compartmentId: "123",
                new ScopeRestriction(KnownResourceTypes.Patient, V1ReadActions, "patient"));
            ExportJobRecord record = CreateExportJobRecord(
                resourceType: KnownResourceTypes.Patient,
                smartCompartmentResourceType: KnownResourceTypes.Patient,
                smartCompartmentId: "456");

            Assert.Throws<UnauthorizedFhirActionException>(() => validator.ValidateJobAccess(record));
        }

        [Fact]
        public void GivenNonSmartRequest_WhenValidatingCreateOrJobAccess_ThenValidationIsNotApplied()
        {
            ExportSmartScopeValidator validator = CreateValidator(
                applyFineGrainedAccessControl: false,
                compartmentResourceType: null,
                compartmentId: null,
                new ScopeRestriction(KnownResourceTypes.Patient, V1ReadActions, "patient"));
            ExportJobRecord record = CreateExportJobRecord(
                resourceType: KnownResourceTypes.Patient,
                smartCompartmentResourceType: null,
                smartCompartmentId: null);

            validator.ValidateJobAccess(record);
        }

        private ExportSmartScopeValidator CreateValidator(
            string compartmentResourceType,
            string compartmentId,
            params ScopeRestriction[] scopeRestrictions)
        {
            return CreateValidator(
                applyFineGrainedAccessControl: true,
                compartmentResourceType,
                compartmentId,
                scopeRestrictions);
        }

        private ExportSmartScopeValidator CreateValidator(
            bool applyFineGrainedAccessControl,
            string compartmentResourceType,
            string compartmentId,
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
            requestContext.AccessControlContext.CompartmentResourceType = compartmentResourceType;
            requestContext.AccessControlContext.CompartmentId = compartmentId;
            foreach (ScopeRestriction scopeRestriction in scopeRestrictions)
            {
                requestContext.AccessControlContext.AllowedResourceActions.Add(scopeRestriction);
            }

            _contextAccessor.RequestContext = requestContext;

            return new ExportSmartScopeValidator(_contextAccessor, _searchService);
        }

        private static CreateExportRequest CreatePatientExportRequest(string patientId, string resourceType)
        {
            return new CreateExportRequest(
                requestUri: new Uri($"http://localhost/Patient/{patientId}/$export"),
                requestType: ExportJobType.Patient,
                resourceType: resourceType,
                patientId: patientId);
        }

        private static ExportJobRecord CreateExportJobRecord(
            string resourceType,
            string smartCompartmentResourceType,
            string smartCompartmentId)
        {
            return new ExportJobRecord(
                requestUri: new Uri("http://localhost/Patient/123/$export?_type=Patient"),
                exportType: ExportJobType.Patient,
                exportFormat: "Patient",
                resourceType: resourceType,
                filters: Array.Empty<ExportJobFilter>(),
                hash: "hash",
                rollingFileSizeInMB: 64,
                patientId: "123",
                smartCompartmentResourceType: smartCompartmentResourceType,
                smartCompartmentId: smartCompartmentId,
                smartRequest: true);
        }
    }
}
