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
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Newtonsoft.Json.Linq;
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
        private readonly IReferenceToElementResolver _referenceToElementResolver = Substitute.For<IReferenceToElementResolver>();
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

            var fhirDataScope = Substitute.For<IScoped<IFhirDataStore>>();
            fhirDataScope.Value.Returns(_fhirDataStore);

            _groupMemberExtractor = new GroupMemberExtractor(
                fhirDataScope,
                _resourceDeserializer,
                _referenceToElementResolver);
        }

        [Fact]
        public async System.Threading.Tasks.Task GivenAGroupId_WhenExecuted_ThenTheMembersIdsAreReturned()
        {
            var structureDefinitionSummaryProvider = new PocoStructureDefinitionSummaryProvider();
            var patientReference = "Patient Reference";
            var observationReference = "Observation Reference";

            _referenceToElementResolver.Resolve(patientReference).Returns(x =>
            {
                ISourceNode node = FhirJsonNode.Create(
                JObject.FromObject(
                    new
                    {
                        resourceType = KnownResourceTypes.Patient,
                        id = patientReference,
                    }));

                return node.ToTypedElement(structureDefinitionSummaryProvider);
            });

            _referenceToElementResolver.Resolve(observationReference).Returns(x =>
            {
                ISourceNode node = FhirJsonNode.Create(
                JObject.FromObject(
                    new
                    {
                        resourceType = KnownResourceTypes.Observation,
                        id = observationReference,
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

            var groupMembers = await _groupMemberExtractor.GetGroupMembers("group", DateTimeOffset.Now, _cancellationToken);

            Assert.Equal(Tuple.Create(patientReference, KnownResourceTypes.Patient), groupMembers[0]);
            Assert.Equal(Tuple.Create(observationReference, KnownResourceTypes.Observation), groupMembers[1]);
            Assert.Equal(2, groupMembers.Count);
        }
    }
}
