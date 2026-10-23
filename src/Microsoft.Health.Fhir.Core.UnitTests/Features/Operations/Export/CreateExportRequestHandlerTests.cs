// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features;
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

        public static IEnumerable<object[]> InclusionFilters()
        {
            foreach (string parameter in new[] { "_include", "_revinclude", "_include:iterate", "_revinclude:iterate", "_include:recurse", "_revinclude:recurse", "_INCLUDE", "_REVINCLUDE:iterate" })
            {
                foreach (string handling in new[] { null, "strict", "lenient" })
                {
                    foreach (bool smartRequest in new[] { false, true })
                    {
                        yield return new object[] { parameter, handling, smartRequest };
                    }
                }
            }
        }

        [Theory]
        [MemberData(nameof(InclusionFilters))]
        public async Task GivenInclusionTypeFilter_WhenCreatingExport_ThenFilterAndIncludePermissionsArePersisted(
            string parameter,
            string handling,
            bool smartRequest)
        {
            // Arrange
            var handler = CreateHandler(handling, smartRequest);
            var request = new CreateExportRequest(
                new Uri("https://localhost/$export"),
                ExportJobType.All,
                resourceType: KnownResourceTypes.Observation,
                filters: $"Observation?status=final&{parameter}=Observation:subject");
            ExportJobRecord persistedJob = null;
            _dataStore.CreateExportJobAsync(Arg.Any<ExportJobRecord>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    persistedJob = call.Arg<ExportJobRecord>();
                    return new ExportJobOutcome(persistedJob, WeakETag.FromVersionId("1"));
                });
            _searchOptionsFactory.Create(Arg.Any<string>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>())
                .Returns(new SearchOptions { UnsupportedSearchParams = Array.Empty<Tuple<string, string>>() });

            // Act
            await handler.HandleAsync(request, CancellationToken.None);

            // Assert
            Assert.NotNull(persistedJob);
            Assert.Equal(KnownResourceTypes.Observation, persistedJob.ResourceType);
            Assert.Equal(Tuple.Create(parameter, "Observation:subject"), Assert.Single(persistedJob.Filters).Parameters[1]);
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
            var handler = CreateHandler("lenient", smartRequest: true);
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
            Assert.Equal(new[] { KnownResourceTypes.Observation, KnownResourceTypes.Patient }, persistedJob.AuthorizedIncludeResourceTypes);
        }

        [Theory]
        [InlineData(null, false)]
        [InlineData("strict", false)]
        [InlineData("lenient", false)]
        [InlineData(null, true)]
        [InlineData("strict", true)]
        [InlineData("lenient", true)]
        public async Task GivenRestrictiveTypeFilter_WhenCreatingExport_ThenObservationOnlyJobIsPersisted(string handling, bool smartRequest)
        {
            // Arrange
            var handler = CreateHandler(handling, smartRequest);
            var request = new CreateExportRequest(
                new Uri("https://localhost/$export"),
                ExportJobType.All,
                resourceType: KnownResourceTypes.Observation,
                filters: "Observation?status=final&subject=Patient/p44");
            ExportJobRecord persistedJob = null;
            _dataStore.CreateExportJobAsync(Arg.Any<ExportJobRecord>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    persistedJob = call.Arg<ExportJobRecord>();
                    return new ExportJobOutcome(persistedJob, WeakETag.FromVersionId("1"));
                });
            _searchOptionsFactory.Create(Arg.Any<string>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>())
                .Returns(new SearchOptions { UnsupportedSearchParams = Array.Empty<Tuple<string, string>>() });

            // Act
            await handler.HandleAsync(request, CancellationToken.None);

            // Assert
            Assert.NotNull(persistedJob);
            Assert.Equal(KnownResourceTypes.Observation, persistedJob.ResourceType);
            var filter = Assert.Single(persistedJob.Filters);
            Assert.Equal(KnownResourceTypes.Observation, filter.ResourceType);
            Assert.Equal(new[] { Tuple.Create("status", "final"), Tuple.Create("subject", "Patient/p44") }, filter.Parameters);
        }

        private CreateExportRequestHandler CreateHandler(string handling, bool smartRequest)
        {
            var requestContext = new FhirRequestContext(
                method: "GET",
                uriString: "https://localhost/$export",
                baseUriString: "https://localhost",
                correlationId: "export-type-filter-test",
                requestHeaders: new Dictionary<string, StringValues>(),
                responseHeaders: new Dictionary<string, StringValues>());
            if (handling != null)
            {
                requestContext.RequestHeaders.Add(KnownHeaders.Prefer, $"respond-async, handling={handling}");
            }

            requestContext.AccessControlContext.ApplyFineGrainedAccessControl = smartRequest;
            requestContext.AccessControlContext.AllowedResourceActions.Add(
                new ScopeRestriction(KnownResourceTypes.Observation, DataActions.ReadById | DataActions.Search | DataActions.Export, "system"));
            _contextAccessor.RequestContext = requestContext;
            var authorizer = new ExportSmartScopeAuthorizer(_contextAccessor, Options.Create(new CoreFeatureConfiguration()));
            var claimsExtractor = Substitute.For<IClaimsExtractor>();
            claimsExtractor.Extract().Returns(Array.Empty<KeyValuePair<string, string>>());

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
