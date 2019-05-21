// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Models;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Compartment
{
    public class CompartmentIndexerTests
    {
        private const string ReferenceSearchTestParam = "referenceSearchTestParam";
        private const string AnotherReferenceSearchTestParam = "referenceSearchTestParam2";
        private const string YetAnotherReferenceSearchTestParam = "referenceSearchTestParam3";
        private const string NonReferenceReferenceSearchTestParam = "nonReferenceSearchTestParam";

        private readonly ICompartmentDefinitionManager _compartmentManager = Substitute.For<ICompartmentDefinitionManager>();
        private readonly CompartmentIndexer _compartmentIndexer;

        public CompartmentIndexerTests()
        {
            _compartmentIndexer = new CompartmentIndexer(_compartmentManager);
        }

        [Theory]
        [InlineData(ResourceType.Observation, CompartmentType.Patient, ReferenceKind.Internal, "123")]
        [InlineData(ResourceType.Claim, CompartmentType.Encounter, ReferenceKind.Internal, "example")]
        [InlineData(ResourceType.Account, CompartmentType.Device, ReferenceKind.Internal, "example1")]
        [InlineData(ResourceType.Appointment, CompartmentType.Practitioner, ReferenceKind.Internal, "example55")]
        [InlineData(ResourceType.Coverage, CompartmentType.RelatedPerson, ReferenceKind.Internal, "example4")]
        [InlineData(ResourceType.ClaimResponse, CompartmentType.Patient, ReferenceKind.External, "sadasd")]
        [InlineData(ResourceType.CareTeam, CompartmentType.Encounter, ReferenceKind.External, "fddsds")]
        [InlineData(ResourceType.Account, CompartmentType.Device, ReferenceKind.External, "t65tdgd5")]
        [InlineData(ResourceType.AllergyIntolerance, CompartmentType.Practitioner, ReferenceKind.InternalOrExternal, "jmjhme554")]
        [InlineData(ResourceType.Procedure, CompartmentType.RelatedPerson, ReferenceKind.InternalOrExternal, "446dvbcvbcv")]
        public void GivenSearchIndicesWithResourceTypeAndCompartmentType_WhenExtracted_ThenCorrectIndicesExtracted(ResourceType resourceType, CompartmentType compartmentType, ReferenceKind referenceKind, string expectedResourceId)
        {
            HashSet<string> compParams = null;
            _compartmentManager.TryGetSearchParams(resourceType, compartmentType, out compParams)
                .Returns(x =>
                {
                    x[2] = new HashSet<string> { ReferenceSearchTestParam, NonReferenceReferenceSearchTestParam };
                    return true;
                });
            var searchIndexEntries = new List<SearchIndexEntry>
            {
                new SearchIndexEntry(ReferenceSearchTestParam, new ReferenceSearchValue(referenceKind, new Uri("http://localhost"), CompartmentDefinitionManager.CompartmentTypeToResourceType(compartmentType), expectedResourceId)),
                new SearchIndexEntry(NonReferenceReferenceSearchTestParam, new StringSearchValue("aadsdas")),
            };
            CompartmentIndices compartmentIndices = _compartmentIndexer.Extract(resourceType, searchIndexEntries);

            Assert.Collection(GetResourceIdsForCompartmentType(compartmentType, compartmentIndices), r => string.Equals(expectedResourceId, r, StringComparison.OrdinalIgnoreCase));
        }

        [Theory]
        [InlineData(ResourceType.Observation, CompartmentType.Patient, ReferenceKind.Internal, "tyythjy")]
        [InlineData(ResourceType.Account, CompartmentType.Device, ReferenceKind.Internal, "jgjghgg")]
        [InlineData(ResourceType.Appointment, CompartmentType.Practitioner, ReferenceKind.Internal, "gnhnhgn")]
        [InlineData(ResourceType.Coverage, CompartmentType.RelatedPerson, ReferenceKind.Internal, "gngngh")]
        [InlineData(ResourceType.ClaimResponse, CompartmentType.Patient, ReferenceKind.External, "ghnvcbcb")]
        [InlineData(ResourceType.CareTeam, CompartmentType.Encounter, ReferenceKind.External, "556656sxc")]
        [InlineData(ResourceType.AllergyIntolerance, CompartmentType.Practitioner, ReferenceKind.InternalOrExternal, "hj56776t6")]
        public void GivenSearchIndicesWithResourceTypeAndCompartmentTypeAndNullReferenceSearchResourceType_WhenExtracted_ThenNoIndicesExtracted(ResourceType resourceType, CompartmentType compartmentType, ReferenceKind referenceKind, string expectedResourceId)
        {
            HashSet<string> compParams = null;
            _compartmentManager.TryGetSearchParams(resourceType, compartmentType, out compParams)
                .Returns(x =>
                {
                    x[2] = new HashSet<string> { ReferenceSearchTestParam };
                    return true;
                });
            var searchIndexEntries = new List<SearchIndexEntry>
            {
                new SearchIndexEntry(ReferenceSearchTestParam, new ReferenceSearchValue(referenceKind, null, null, expectedResourceId)),
            };
            CompartmentIndices compartmentIndices = _compartmentIndexer.Extract(resourceType, searchIndexEntries);

            Assert.Null(GetResourceIdsForCompartmentType(compartmentType, compartmentIndices));
        }

        [Theory]
        [InlineData(CompartmentType.Patient, CompartmentType.Encounter, CompartmentType.RelatedPerson)]
        [InlineData(CompartmentType.RelatedPerson, CompartmentType.Practitioner, CompartmentType.Patient)]
        public void GivenSearchIndicesWithMultipleReferenceSearchParams_WhenExtracted_ThenCorrectIndicesExtracted(CompartmentType expectedCompartmentType1, CompartmentType expectedCompartmentType2, CompartmentType expectedCompartmentType3)
        {
            HashSet<string> compParams = null;
            const ResourceType ExpectedCareTeamResource = ResourceType.CareTeam;

            // Setup the compartment search definitions.
            _compartmentManager.TryGetSearchParams(ExpectedCareTeamResource, expectedCompartmentType1, out compParams)
                .Returns(x =>
                {
                    x[2] = new HashSet<string> { ReferenceSearchTestParam };
                    return true;
                });
            _compartmentManager.TryGetSearchParams(ExpectedCareTeamResource, expectedCompartmentType2, out compParams)
                .Returns(x =>
                {
                    x[2] = new HashSet<string> { AnotherReferenceSearchTestParam };
                    return true;
                });
            _compartmentManager.TryGetSearchParams(ExpectedCareTeamResource, expectedCompartmentType3, out compParams)
                .Returns(x =>
                {
                    x[2] = new HashSet<string> { YetAnotherReferenceSearchTestParam };
                    return true;
                });
            const string ExpectedResourceIdR1 = "r1";
            const string ExpectedResourceIdR2 = "r2";
            const string ExpectedResourceIdR3 = "r3";

            // Setup multiple reference search parameters with expected resource ids for the compartments.
            var searchIndexEntries = new List<SearchIndexEntry>
            {
                new SearchIndexEntry(ReferenceSearchTestParam, new ReferenceSearchValue(ReferenceKind.InternalOrExternal, new Uri("http://localhost"), CompartmentDefinitionManager.CompartmentTypeToResourceType(expectedCompartmentType1), ExpectedResourceIdR1)),
                new SearchIndexEntry(AnotherReferenceSearchTestParam, new ReferenceSearchValue(ReferenceKind.Internal, new Uri("http://localhost"), CompartmentDefinitionManager.CompartmentTypeToResourceType(expectedCompartmentType2), ExpectedResourceIdR2)),
                new SearchIndexEntry(YetAnotherReferenceSearchTestParam, new ReferenceSearchValue(ReferenceKind.External, new Uri("http://localhost"), CompartmentDefinitionManager.CompartmentTypeToResourceType(expectedCompartmentType3), ExpectedResourceIdR3)),
            };

            CompartmentIndices compartmentIndices = _compartmentIndexer.Extract(ExpectedCareTeamResource, searchIndexEntries);

            Assert.Collection(GetResourceIdsForCompartmentType(expectedCompartmentType1, compartmentIndices), r => string.Equals(ExpectedResourceIdR1, r, StringComparison.OrdinalIgnoreCase));
            Assert.Collection(GetResourceIdsForCompartmentType(expectedCompartmentType2, compartmentIndices), r => string.Equals(ExpectedResourceIdR2, r, StringComparison.OrdinalIgnoreCase));
            Assert.Collection(GetResourceIdsForCompartmentType(expectedCompartmentType3, compartmentIndices), r => string.Equals(ExpectedResourceIdR3, r, StringComparison.OrdinalIgnoreCase));
        }

        private static IReadOnlyCollection<string> GetResourceIdsForCompartmentType(CompartmentType compartmentType, CompartmentIndices compartmentIndices)
        {
            IReadOnlyCollection<string> resourceIds = null;
            if (compartmentType == CompartmentType.Device)
            {
                resourceIds = compartmentIndices.DeviceCompartmentEntry;
            }
            else if (compartmentType == CompartmentType.Encounter)
            {
                resourceIds = compartmentIndices.EncounterCompartmentEntry;
            }
            else if (compartmentType == CompartmentType.Patient)
            {
                resourceIds = compartmentIndices.PatientCompartmentEntry;
            }
            else if (compartmentType == CompartmentType.Practitioner)
            {
                resourceIds = compartmentIndices.PractitionerCompartmentEntry;
            }
            else if (compartmentType == CompartmentType.RelatedPerson)
            {
                resourceIds = compartmentIndices.RelatedPersonCompartmentEntry;
            }

            return resourceIds;
        }
    }
}
