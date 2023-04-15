// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Hl7.Fhir.Model;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Operations.SearchParameterState;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Messages.SearchParameterState;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.UnitTests.Extensions;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using static Hl7.Fhir.Model.VerificationResult;

namespace Microsoft.Health.Fhir.Shared.Core.UnitTests.Features.Operations.SearchParameterState
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.SearchParameterStatus)]
    public class SearchParameterStateUpdateHandlerTests
    {
        private static readonly string ResourceId = "http://hl7.org/fhir/SearchParameter/Resource-id";
        private static readonly string ResourceLastUpdated = "http://hl7.org/fhir/SearchParameter/Resource-lastUpdated";
        private static readonly string ResourceProfile = "http://hl7.org/fhir/SearchParameter/Resource-profile";
        private static readonly string ResourceSecurity = "http://hl7.org/fhir/SearchParameter/Resource-security";
        private static readonly string ResourceQuery = "http://hl7.org/fhir/SearchParameter/Resource-query";
        private static readonly string ResourceTest = "http://hl7.org/fhir/SearchParameter/Resource-test";
        private static readonly string PatientPreExisting2 = "http://test/Patient-preexisting2";
        private static readonly string PatientLastUpdated = "http://test/Patient-lastupdated";
        private static readonly string NotFoundResource = "http://test/not-here";

        private readonly IAuthorizationService<DataActions> _authorizationService = Substitute.For<IAuthorizationService<DataActions>>();
        private readonly SearchParameterStateUpdateHandler _searchParameterStateUpdateHandler;
        private readonly SearchParameterStatusManager _searchParameterStatusManager;
        private readonly ISearchParameterStatusDataStore _searchParameterStatusDataStore = Substitute.For<ISearchParameterStatusDataStore>();
        private readonly SearchParameterDefinitionManager _searchParameterDefinitionManager;
        private readonly ISearchParameterSupportResolver _searchParameterSupportResolver = Substitute.For<ISearchParameterSupportResolver>();
        private readonly IMediator _mediator = Substitute.For<IMediator>();
        private readonly CancellationToken _cancellationToken = default;
        private ISearchService _searchService = Substitute.For<ISearchService>();
        private readonly ILogger<SearchParameterStatusManager> _logger = Substitute.For<ILogger<SearchParameterStatusManager>>();

        public SearchParameterStateUpdateHandlerTests()
        {
            _searchParameterDefinitionManager = Substitute.For<SearchParameterDefinitionManager>(ModelInfoProvider.Instance, _mediator, () => _searchService.CreateMockScope(), NullLogger<SearchParameterDefinitionManager>.Instance);
            _searchParameterStatusManager = new SearchParameterStatusManager(_searchParameterStatusDataStore, _searchParameterDefinitionManager, _searchParameterSupportResolver, _mediator, _logger);
            _searchParameterStateUpdateHandler = new SearchParameterStateUpdateHandler(_authorizationService, _searchParameterStatusManager);
            _cancellationToken = CancellationToken.None;

            _authorizationService.CheckAccess(DataActions.Reindex, _cancellationToken).Returns(DataActions.Reindex);
            var searchParamDefinitionStore = new List<SearchParameterInfo>
            {
                new SearchParameterInfo(
                    "Resource",
                    "id",
                    ValueSets.SearchParamType.Token,
                    new Uri(ResourceId),
                    new List<SearchParameterComponentInfo> { new SearchParameterComponentInfo(new Uri(ResourceId)) }),
                new SearchParameterInfo(
                    "Resource",
                    "lastUpdated",
                    ValueSets.SearchParamType.Token,
                    new Uri(ResourceLastUpdated),
                    new List<SearchParameterComponentInfo> { new SearchParameterComponentInfo(new Uri(ResourceLastUpdated)) }),
                new SearchParameterInfo(
                    "Resource",
                    "profile",
                    ValueSets.SearchParamType.Token,
                    new Uri(ResourceProfile),
                    new List<SearchParameterComponentInfo> { new SearchParameterComponentInfo(new Uri(ResourceProfile)) }),
                new SearchParameterInfo(
                    "Resource",
                    "security",
                    ValueSets.SearchParamType.Token,
                    new Uri(ResourceSecurity),
                    new List<SearchParameterComponentInfo> { new SearchParameterComponentInfo(new Uri(ResourceSecurity)) }),
                new SearchParameterInfo(
                    "Resource",
                    "query",
                    ValueSets.SearchParamType.Token,
                    new Uri(ResourceQuery),
                    new List<SearchParameterComponentInfo> { new SearchParameterComponentInfo(new Uri(ResourceQuery)) }),
                new SearchParameterInfo(
                    "Resource",
                    "test",
                    ValueSets.SearchParamType.Token,
                    new Uri(ResourceTest),
                    new List<SearchParameterComponentInfo> { new SearchParameterComponentInfo(new Uri(ResourceTest)) }),
                new SearchParameterInfo(
                    "Patient",
                    "preexisting2",
                    ValueSets.SearchParamType.Token,
                    new Uri(PatientPreExisting2),
                    new List<SearchParameterComponentInfo> { new SearchParameterComponentInfo(new Uri(PatientPreExisting2)) }),
                new SearchParameterInfo(
                    "Patient",
                    "lastUpdated",
                    ValueSets.SearchParamType.Token,
                    new Uri(PatientLastUpdated),
                    new List<SearchParameterComponentInfo> { new SearchParameterComponentInfo(new Uri(PatientLastUpdated)) }),
            };

            _searchParameterStatusDataStore.GetSearchParameterStatuses(Arg.Any<CancellationToken>())
                .Returns(new[]
                {
                    new ResourceSearchParameterStatus
                    {
                        Status = SearchParameterStatus.Enabled,
                        Uri = new Uri(ResourceId),
                    },
                    new ResourceSearchParameterStatus
                    {
                        Status = SearchParameterStatus.PendingDisable,
                        Uri = new Uri(ResourceLastUpdated),
                        IsPartiallySupported = true,
                    },
                    new ResourceSearchParameterStatus
                    {
                        Status = SearchParameterStatus.Disabled,
                        Uri = new Uri(ResourceProfile),
                    },
                    new ResourceSearchParameterStatus
                    {
                        Status = SearchParameterStatus.Supported,
                        Uri = new Uri(ResourceSecurity),
                    },
                    new ResourceSearchParameterStatus
                    {
                        Status = SearchParameterStatus.Enabled,
                        Uri = new Uri(PatientPreExisting2),
                    },
                    new ResourceSearchParameterStatus
                    {
                        Status = SearchParameterStatus.PendingDelete,
                        Uri = new Uri(ResourceQuery),
                    },
                    new ResourceSearchParameterStatus
                    {
                        Status = SearchParameterStatus.Deleted,
                        Uri = new Uri(ResourceTest),
                    },
                    new ResourceSearchParameterStatus
                    {
                        Status = SearchParameterStatus.Deleted,
                        Uri = new Uri(PatientLastUpdated),
                    },
                });
            ConcurrentDictionary<string, SearchParameterInfo> urlLookup = new ConcurrentDictionary<string, SearchParameterInfo>(searchParamDefinitionStore.ToDictionary(x => x.Url.ToString()));
            _searchParameterDefinitionManager.UrlLookup = urlLookup;
            ConcurrentDictionary<string, ConcurrentDictionary<string, SearchParameterInfo>> typeLookup = new ConcurrentDictionary<string, ConcurrentDictionary<string, SearchParameterInfo>>();
            typeLookup.GetOrAdd("Resource", new ConcurrentDictionary<string, SearchParameterInfo>(searchParamDefinitionStore.Where(sp => sp.Name == "Resource").ToDictionary(x => x.Code.ToString())));
            typeLookup.GetOrAdd("Patient", new ConcurrentDictionary<string, SearchParameterInfo>(searchParamDefinitionStore.Where(sp => sp.Name == "Patient").ToDictionary(x => x.Code.ToString())));
            _searchParameterDefinitionManager.TypeLookup = typeLookup;

            // _searchParameterStatusManager.UpdateSearchParameterStatusAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<SearchParameterStatus>(), Arg.Any<CancellationToken>()).Returns(System.Threading.Tasks.Task.CompletedTask);
        }

        [Fact]
        public async void GivenARequestToUpdateSearchParameterStatus_WhenTheStatusIsEnabled_ThenTheStatusShouldBeUpdated()
        {
            List<Tuple<Uri, SearchParameterStatus>> updates = new List<Tuple<Uri, SearchParameterStatus>>()
            {
                new Tuple<Uri, SearchParameterStatus>(new Uri(ResourceId), SearchParameterStatus.Supported),
            };

            SearchParameterStateUpdateResponse response = await _searchParameterStateUpdateHandler.Handle(new SearchParameterStateUpdateRequest(updates), default);

            Assert.NotNull(response);
            Assert.NotNull(response.UpdateStatus);

            var unwrappedResponse = response.UpdateStatus.ToPoco<Hl7.Fhir.Model.Bundle>();
            var resourceResponse = (Parameters)unwrappedResponse.Entry[0].Resource;
            var urlPart = resourceResponse.Parameter[0].Part.Where(p => p.Name == SearchParameterStateProperties.Url).First();
            var statusPart = resourceResponse.Parameter[0].Part.Where(p => p.Name == SearchParameterStateProperties.Status).First();
            Assert.True(urlPart.Value.ToString() == ResourceId);
            Assert.True(statusPart.Value.ToString() == SearchParameterStatus.Supported.ToString());
        }

        [Fact]
        public async void GivenARequestToUpdateSearchParameterStatus_WhenTheResourceIsNotFound_ThenAnOperationOutcomeIsReturnedWithInformationSeverity()
        {
            List<Tuple<Uri, SearchParameterStatus>> updates = new List<Tuple<Uri, SearchParameterStatus>>()
            {
                new Tuple<Uri, SearchParameterStatus>(new Uri(NotFoundResource), SearchParameterStatus.Enabled),
            };

            SearchParameterStateUpdateResponse response = await _searchParameterStateUpdateHandler.Handle(new SearchParameterStateUpdateRequest(updates), default);

            Assert.NotNull(response);
            Assert.NotNull(response.UpdateStatus);

            var unwrappedResponse = response.UpdateStatus.ToPoco<Hl7.Fhir.Model.Bundle>();
            var resourceResponse = (OperationOutcome)unwrappedResponse.Entry[0].Resource;
            var issue = resourceResponse.Issue[0];
            Assert.True(issue.Details.Text == string.Format(Fhir.Core.Resources.SearchParameterNotFound, SearchParameterStatus.Enabled, NotFoundResource));
            Assert.True(issue.Severity == OperationOutcome.IssueSeverity.Information);
        }

        [Fact]
        public async void GivenARequestToUpdateSearchParameterStatus_WhenStatusIsNotSupportedOrDisabled_ThenAnOperationOutcomeIsReturnedWithErrorSeverity()
        {
            List<Tuple<Uri, SearchParameterStatus>> updates = new List<Tuple<Uri, SearchParameterStatus>>()
            {
                new Tuple<Uri, SearchParameterStatus>(new Uri(ResourceId), SearchParameterStatus.Deleted),
            };

            SearchParameterStateUpdateResponse response = await _searchParameterStateUpdateHandler.Handle(new SearchParameterStateUpdateRequest(updates), default);

            Assert.NotNull(response);
            Assert.NotNull(response.UpdateStatus);

            var unwrappedResponse = response.UpdateStatus.ToPoco<Hl7.Fhir.Model.Bundle>();
            var resourceResponse = (OperationOutcome)unwrappedResponse.Entry[0].Resource;
            var issue = resourceResponse.Issue[0];
            Assert.True(issue.Details.Text == string.Format(Fhir.Core.Resources.InvalidUpdateStatus, SearchParameterStatus.Deleted, ResourceId));
            Assert.True(issue.Severity == OperationOutcome.IssueSeverity.Error);
        }

        [Fact]
        public async void GivenARequestToUpdateSearchParameterStatus_WhenStatusIsDisabled_ThenTheStatusIsUpdatedAsPendingDisable()
        {
            List<Tuple<Uri, SearchParameterStatus>> updates = new List<Tuple<Uri, SearchParameterStatus>>()
            {
                new Tuple<Uri, SearchParameterStatus>(new Uri(ResourceId), SearchParameterStatus.Disabled),
            };

            SearchParameterStateUpdateResponse response = await _searchParameterStateUpdateHandler.Handle(new SearchParameterStateUpdateRequest(updates), default);

            Assert.NotNull(response);
            Assert.NotNull(response.UpdateStatus);

            var unwrappedResponse = response.UpdateStatus.ToPoco<Hl7.Fhir.Model.Bundle>();
            var resourceResponse = (Parameters)unwrappedResponse.Entry[0].Resource;
            var urlPart = resourceResponse.Parameter[0].Part.Where(p => p.Name == SearchParameterStateProperties.Url).First();
            var statusPart = resourceResponse.Parameter[0].Part.Where(p => p.Name == SearchParameterStateProperties.Status).First();
            Assert.True(urlPart.Value.ToString() == ResourceId);
            Assert.True(statusPart.Value.ToString() == SearchParameterStatus.PendingDisable.ToString());
        }
    }
}
