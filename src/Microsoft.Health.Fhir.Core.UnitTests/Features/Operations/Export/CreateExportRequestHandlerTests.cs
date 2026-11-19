// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Operations.Security;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Messages.Export;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.Export
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Export)]
    public class CreateExportRequestHandlerTests
    {
        private readonly IFhirOperationDataStore _dataStore = Substitute.For<IFhirOperationDataStore>();
        private readonly ISearchOptionsFactory _searchOptionsFactory = Substitute.For<ISearchOptionsFactory>();
        private readonly RequestContextAccessor<IFhirRequestContext> _contextAccessor = new FhirRequestContextAccessor();

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task GivenInclusionTypeFilter_WhenCreatingExport_ThenFilterAndIncludePermissionsArePersisted(bool smartRequest)
        {
            // Arrange
            var handler = CreateHandler(smartRequest);
            var request = new CreateExportRequest(
                new Uri("https://localhost/$export"),
                ExportJobType.All,
                resourceType: KnownResourceTypes.Observation,
                filters: "Observation?status=final&_include=Observation:subject");
            ExportJobRecord persistedJob = null;
            _dataStore.CreateExportJobAsync(Arg.Any<ExportJobRecord>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    persistedJob = call.Arg<ExportJobRecord>();
                    return new ExportJobOutcome(persistedJob, WeakETag.FromVersionId("1"));
                });

            // Act
            await handler.HandleAsync(request, CancellationToken.None);

            // Assert
            Assert.NotNull(persistedJob);
            Assert.Equal(KnownResourceTypes.Observation, persistedJob.ResourceType);
            Assert.Equal(
                new[] { Tuple.Create("status", "final"), Tuple.Create("_include", "Observation:subject") },
                Assert.Single(persistedJob.Filters).Parameters);
            Assert.Equal(smartRequest, persistedJob.SmartRequest);
            if (smartRequest)
            {
                Assert.Equal(new[] { KnownResourceTypes.Observation }, persistedJob.AuthorizedIncludeResourceTypes);
            }
            else
            {
                Assert.Null(persistedJob.AuthorizedIncludeResourceTypes);
            }
        }

        [Fact]
        public async Task GivenAuthorizedInclusionOutsideRequestedType_WhenCreatingExport_ThenInclusionPermissionIsPersisted()
        {
            // Arrange
            var handler = CreateHandler(smartRequest: true);
            _contextAccessor.RequestContext.AccessControlContext.AllowedResourceActions.Add(
                new ScopeRestriction(KnownResourceTypes.Patient, DataActions.Read | DataActions.Export, "system"));
            var request = new CreateExportRequest(
                new Uri("https://localhost/$export"),
                ExportJobType.All,
                resourceType: KnownResourceTypes.Observation,
                filters: "Observation?_include=Observation:subject");
            ExportJobRecord persistedJob = null;
            _dataStore.CreateExportJobAsync(Arg.Any<ExportJobRecord>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    persistedJob = call.Arg<ExportJobRecord>();
                    return new ExportJobOutcome(persistedJob, WeakETag.FromVersionId("1"));
                });

            // Act
            await handler.HandleAsync(request, CancellationToken.None);

            // Assert
            Assert.Equal(KnownResourceTypes.Observation, persistedJob.ResourceType);
            Assert.Equal(
                new[] { KnownResourceTypes.Observation, KnownResourceTypes.Patient }.OrderBy(type => type, StringComparer.Ordinal),
                persistedJob.AuthorizedIncludeResourceTypes.OrderBy(type => type, StringComparer.Ordinal));
        }

        private CreateExportRequestHandler CreateHandler(bool smartRequest)
        {
            var requestContext = new FhirRequestContext(
                method: "GET",
                uriString: "https://localhost/$export",
                baseUriString: "https://localhost",
                correlationId: "export-type-filter-test",
                requestHeaders: new Dictionary<string, StringValues>(),
                responseHeaders: new Dictionary<string, StringValues>());
            requestContext.AccessControlContext.ApplyFineGrainedAccessControl = smartRequest;
            requestContext.AccessControlContext.AllowedResourceActions.Add(
                new ScopeRestriction(KnownResourceTypes.Observation, DataActions.ReadById | DataActions.Search | DataActions.Export, "system"));
            _contextAccessor.RequestContext = requestContext;
            var authorizer = new ExportSmartScopeAuthorizer(_contextAccessor, Options.Create(new CoreFeatureConfiguration()));
            var claimsExtractor = Substitute.For<IClaimsExtractor>();
            claimsExtractor.Extract().Returns(Array.Empty<KeyValuePair<string, string>>());
            _searchOptionsFactory.Create(Arg.Any<string>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>())
                .Returns(new SearchOptions { UnsupportedSearchParams = Array.Empty<Tuple<string, string>>() });

            return new CreateExportRequestHandler(
                claimsExtractor,
                _dataStore,
                DisabledFhirAuthorizationService.Instance,
                Options.Create(new ExportJobConfiguration()),
                _contextAccessor,
                _searchOptionsFactory,
                authorizer,
                NullLogger<CreateExportRequestHandler>.Instance);
        }
    }
}
