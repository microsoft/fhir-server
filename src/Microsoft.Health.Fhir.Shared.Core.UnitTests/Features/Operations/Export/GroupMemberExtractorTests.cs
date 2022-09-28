// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Hl7.Fhir.Specification;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.UnitTests.Extensions;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.Export
{
    [Trait("Traits.OwningTeam", OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Export)]
    public class GroupMemberExtractorTests
    {
        private GroupMemberExtractor _groupMemberExtractor;
        private ResourceElement _resourceElement;

        private readonly IFhirDataStore _fhirDataStore = Substitute.For<IFhirDataStore>();
        private readonly ResourceDeserializer _resourceDeserializer;
        private readonly IReferenceToElementResolver _referenceToElementResolver = Substitute.For<IReferenceToElementResolver>();
        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        private readonly CancellationToken _cancellationToken;

        private readonly string _patientReference = "Patient Reference";
        private readonly string _secondPatientReference = "Patient Reference 2";
        private readonly string _observationReference = "Observation Reference";
        private readonly string _secondObservationReference = "Observation Reference 2";

        private readonly DateTimeOffset _allResources = new DateTimeOffset(new DateTime(2007, 1, 1));

        public GroupMemberExtractorTests()
        {
            _cancellationToken = _cancellationTokenSource.Token;

            var resourceWrapper = new ResourceWrapper("test", "test", "test", new RawResource("test", FhirResourceFormat.Json, isMetaSet: false), null, DateTimeOffset.Now, false, null, null, null);
            _fhirDataStore.GetAsync(default, default).ReturnsForAnyArgs(x =>
            {
                return System.Threading.Tasks.Task.FromResult(resourceWrapper);
            });

            _resourceDeserializer = new ResourceDeserializer(
                (FhirResourceFormat.Json, new Func<string, string, DateTimeOffset, ResourceElement>((str, version, lastUpdated) =>
                {
                    return _resourceElement;
                })));

            _groupMemberExtractor = new GroupMemberExtractor(
                () => _fhirDataStore.CreateMockScope(),
                _resourceDeserializer,
                _referenceToElementResolver);
        }

        [Fact]
        public async System.Threading.Tasks.Task GivenAGroupId_WhenPatientIdsAreRequested_ThenThePatientsIdsAreReturned()
        {
            SetUpGroupMock();

            var patientSet = await _groupMemberExtractor.GetGroupPatientIds("group", _allResources, _cancellationToken);

            Assert.Single(patientSet);
            Assert.Contains(_patientReference, patientSet);
        }

        [Fact]
        public async System.Threading.Tasks.Task GivenAGroupIdForAGroupWithANestedGroup_WhenPatientIdsAreRequested_ThenThePatientsIdsAreReturned()
        {
            var structureDefinitionSummaryProvider = new PocoStructureDefinitionSummaryProvider();
            var nestedGroupReference = "Nested Group";
            var patientReference2 = "Second Patient";

            _referenceToElementResolver.Resolve(_patientReference).Returns(x =>
            {
                ISourceNode node = FhirJsonNode.Create(
                JObject.FromObject(
                    new
                    {
                        resourceType = KnownResourceTypes.Patient,
                        id = _patientReference,
                    }));

                return node.ToTypedElement(structureDefinitionSummaryProvider);
            });

            _referenceToElementResolver.Resolve(nestedGroupReference).Returns(x =>
            {
                ISourceNode node = FhirJsonNode.Create(
                JObject.FromObject(
                    new
                    {
                        resourceType = KnownResourceTypes.Group,
                        id = nestedGroupReference,
                    }));

                return node.ToTypedElement(structureDefinitionSummaryProvider);
            });

            _referenceToElementResolver.Resolve(patientReference2).Returns(x =>
            {
                ISourceNode node = FhirJsonNode.Create(
                JObject.FromObject(
                    new
                    {
                        resourceType = KnownResourceTypes.Patient,
                        id = patientReference2,
                    }));

                return node.ToTypedElement(structureDefinitionSummaryProvider);
            });

            var group = new Group()
            {
                Member = new List<Group.MemberComponent>()
                {
                    new Group.MemberComponent()
                    {
                        Entity = new ResourceReference()
                        {
                            Reference = _patientReference,
                        },
                    },
                    new Group.MemberComponent()
                    {
                        Entity = new ResourceReference()
                        {
                            Reference = nestedGroupReference,
                        },
                    },
                },
            };

            var nestedGroup = new Group()
            {
                Member = new List<Group.MemberComponent>()
                {
                    new Group.MemberComponent()
                    {
                        Entity = new ResourceReference()
                        {
                            Reference = patientReference2,
                        },
                    },
                },
            };

            var callCount = 0;
            var resourceDeserializer = new ResourceDeserializer(
                (FhirResourceFormat.Json, new Func<string, string, DateTimeOffset, ResourceElement>((str, version, lastUpdated) =>
                {
                    callCount++;
                    if (callCount == 1)
                    {
                        return new ResourceElement(Substitute.For<ITypedElement>(), group);
                    }
                    else
                    {
                        return new ResourceElement(Substitute.For<ITypedElement>(), nestedGroup);
                    }
                })));

            _groupMemberExtractor = new GroupMemberExtractor(
                () => _fhirDataStore.CreateMockScope(),
                resourceDeserializer,
                _referenceToElementResolver);

            var patientSet = await _groupMemberExtractor.GetGroupPatientIds("group", _allResources, _cancellationToken);

            Assert.Equal(2, patientSet.Count);
            Assert.Contains(_patientReference, patientSet);
            Assert.Contains(patientReference2, patientSet);
        }

        [Fact]
        public async System.Threading.Tasks.Task GivenAGroupId_WhenActiveGroupMembersAreRequested_ThenTheActiveMembersIdsAreReturned()
        {
            SetUpGroupMock();

            var groupMembers = await _groupMemberExtractor.GetGroupMembers("group", _allResources, _cancellationToken);

            Assert.Equal(Tuple.Create(_patientReference, KnownResourceTypes.Patient), groupMembers[0]);
            Assert.Equal(Tuple.Create(_observationReference, KnownResourceTypes.Observation), groupMembers[1]);
            Assert.Equal(Tuple.Create(_secondObservationReference, KnownResourceTypes.Observation), groupMembers[2]);
            Assert.Equal(3, groupMembers.Count);
        }

        [Theory]
        [InlineData(2003, 1)]
        [InlineData(2007, 3)]
        [InlineData(2012, 2)]
        public async System.Threading.Tasks.Task GivenAGroupId_WhenGroupMembersAreRequestedFromATime_ThenMembersIdsFromThatTimeAreReturned(int year, int numberOfResources)
        {
            SetUpGroupMock();

            var groupMembers = await _groupMemberExtractor.GetGroupMembers("group", new DateTimeOffset(new DateTime(year, 1, 1)), _cancellationToken);

            Assert.Equal(numberOfResources, groupMembers.Count);
        }

        private void SetUpGroupMock()
        {
            var structureDefinitionSummaryProvider = new PocoStructureDefinitionSummaryProvider();

            _referenceToElementResolver.Resolve(_patientReference).Returns(x =>
            {
                ISourceNode node = FhirJsonNode.Create(
                JObject.FromObject(
                    new
                    {
                        resourceType = KnownResourceTypes.Patient,
                        id = _patientReference,
                    }));

                return node.ToTypedElement(structureDefinitionSummaryProvider);
            });

            _referenceToElementResolver.Resolve(_secondPatientReference).Returns(x =>
            {
                ISourceNode node = FhirJsonNode.Create(
                JObject.FromObject(
                    new
                    {
                        resourceType = KnownResourceTypes.Patient,
                        id = _secondPatientReference,
                    }));

                return node.ToTypedElement(structureDefinitionSummaryProvider);
            });

            _referenceToElementResolver.Resolve(_observationReference).Returns(x =>
            {
                ISourceNode node = FhirJsonNode.Create(
                JObject.FromObject(
                    new
                    {
                        resourceType = KnownResourceTypes.Observation,
                        id = _observationReference,
                    }));

                return node.ToTypedElement(structureDefinitionSummaryProvider);
            });

            _referenceToElementResolver.Resolve(_secondObservationReference).Returns(x =>
            {
                ISourceNode node = FhirJsonNode.Create(
                JObject.FromObject(
                    new
                    {
                        resourceType = KnownResourceTypes.Observation,
                        id = _secondObservationReference,
                    }));

                return node.ToTypedElement(structureDefinitionSummaryProvider);
            });

            var group = new Group()
            {
                Member = new List<Group.MemberComponent>()
                {
                    new Group.MemberComponent()
                    {
                        Entity = new ResourceReference()
                        {
                            Reference = _patientReference,
                        },
                        Period = new Period(new FhirDateTime(2000), new FhirDateTime(2010)),
                    },
                    new Group.MemberComponent()
                    {
                        Entity = new ResourceReference()
                        {
                            Reference = _secondPatientReference,
                        },
                        Period = new Period(new FhirDateTime(2005), new FhirDateTime(2015)),
                        Inactive = true,
                    },
                    new Group.MemberComponent()
                    {
                        Entity = new ResourceReference()
                        {
                            Reference = _observationReference,
                        },
                        Period = new Period(new FhirDateTime(2005), new FhirDateTime(2015)),
                    },
                    new Group.MemberComponent()
                    {
                        Entity = new ResourceReference()
                        {
                            Reference = _secondObservationReference,
                        },
                        Period = new Period(new FhirDateTime(2005), new FhirDateTime(2015)),
                    },
                },
            };

            _resourceElement = new ResourceElement(Substitute.For<ITypedElement>(), group);
        }
    }
}
