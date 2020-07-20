// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.Export
{
    public class GroupMemberExtractorTests
    {
        private GroupMemberExtractor _groupMemberExtractor;
        private ResourceElement _resourceElement;

        private readonly IFhirDataStore _fhirDataStore = Substitute.For<IFhirDataStore>();
        private readonly ResourceDeserializer _resourceDeserializer;
        private readonly ISearchService _searchService = Substitute.For<ISearchService>();
        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        private readonly CancellationToken _cancellationToken;

        public GroupMemberExtractorTests()
        {
            _cancellationToken = _cancellationTokenSource.Token;

            var resourceWrapper = new ResourceWrapper("test", "test", "test", new RawResource("test", FhirResourceFormat.Json), null, DateTimeOffset.Now, false, null, null, null);
            _fhirDataStore.GetAsync(default, default).ReturnsForAnyArgs(x =>
            {
                return System.Threading.Tasks.Task.FromResult(resourceWrapper);
            });

            _resourceDeserializer = new ResourceDeserializer(
                (FhirResourceFormat.Json, new Func<string, string, DateTimeOffset, ResourceElement>((str, version, lastUpdated) =>
                {
                    return _resourceElement;
                })));

            var searchScope = Substitute.For<IScoped<ISearchService>>();
            searchScope.Value.Returns(_searchService);

            var fhirDataScope = Substitute.For<IScoped<IFhirDataStore>>();
            fhirDataScope.Value.Returns(_fhirDataStore);

            _groupMemberExtractor = new GroupMemberExtractor(
                fhirDataScope,
                _resourceDeserializer,
                () => searchScope);
        }

        [Fact]
        public async System.Threading.Tasks.Task GivenAGroupId_WhenExecuted_ThenTheMembersIdsAreReturned()
        {
            var patientReference = "Patient Reference";
            var observationReference = "Observation Reference";

            var group = new Group()
            {
                Member = new System.Collections.Generic.List<Group.MemberComponent>()
                {
                    new Group.MemberComponent()
                    {
                        Entity = new ResourceReference()
                        {
                            Reference = patientReference,
                        },
                    },
                    new Group.MemberComponent()
                    {
                        Entity = new ResourceReference()
                        {
                            Reference = observationReference,
                        },
                    },
                },
            };

            _resourceElement = new ResourceElement(Substitute.For<ITypedElement>(), group);

            _searchService.SearchAsync(
                null,
                Arg.Is(CreateQueryParametersExpression(patientReference)),
                _cancellationToken)
                .Returns(x => CreateSearchResult(KnownResourceTypes.Patient, patientReference));

            _searchService.SearchAsync(
                null,
                Arg.Is(CreateQueryParametersExpression(observationReference)),
                _cancellationToken)
                .Returns(x => CreateSearchResult(KnownResourceTypes.Observation, observationReference));

            var groupMembers = await _groupMemberExtractor.GetGroupMembers("group", _cancellationToken);

            Assert.Equal(Tuple.Create(patientReference, KnownResourceTypes.Patient), groupMembers[0]);
            Assert.Equal(Tuple.Create(observationReference, KnownResourceTypes.Observation), groupMembers[1]);
            Assert.Equal(2, groupMembers.Count);
        }

        private Expression<Predicate<IReadOnlyList<Tuple<string, string>>>> CreateQueryParametersExpression(string id)
        {
            return arg => arg != null && Tuple.Create(KnownQueryParameterNames.Id, id).Equals(arg[0]);
        }

        private SearchResult CreateSearchResult(string resourceType, string id)
        {
            var entry = new SearchResultEntry(
                        new ResourceWrapper(
                            id,
                            "1",
                            resourceType,
                            new RawResource("data", Core.Models.FhirResourceFormat.Json),
                            null,
                            DateTimeOffset.MinValue,
                            false,
                            null,
                            null,
                            null));
            return new SearchResult(new SearchResultEntry[] { entry }, new Tuple<string, string>[0], Array.Empty<(string parameterName, string reason)>(), null);
        }
    }
}
