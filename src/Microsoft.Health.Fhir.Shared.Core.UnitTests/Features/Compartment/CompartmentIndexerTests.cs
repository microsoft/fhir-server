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
using CompartmentType = Microsoft.Health.Fhir.ValueSets.CompartmentType;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Compartment
{
    public class CompartmentIndexerTests
    {
        private readonly SearchParameterInfo _referenceSearchTestParam = new SearchParameterInfo("referenceSearchTestParam");
        private readonly SearchParameterInfo _anotherReferenceSearchTestParam = new SearchParameterInfo("referenceSearchTestParam2");
        private readonly SearchParameterInfo _yetAnotherReferenceSearchTestParam = new SearchParameterInfo("referenceSearchTestParam3");
        private readonly SearchParameterInfo _nonReferenceReferenceSearchTestParam = new SearchParameterInfo("nonReferenceSearchTestParam");

        private readonly ICompartmentDefinitionManager _compartmentManager = Substitute.For<ICompartmentDefinitionManager>();
        private readonly CompartmentIndexer _compartmentIndexer;
        private readonly Uri _baseUri = new Uri("http://localhost");

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
            _compartmentManager.TryGetSearchParams(resourceType.ToString(), compartmentType, out compParams)
                .Returns(x =>
                {
                    x[2] = new HashSet<string> { _referenceSearchTestParam.Name, _nonReferenceReferenceSearchTestParam.Name };
                    return true;
                });
            var searchIndexEntries = new List<SearchIndexEntry>
            {
                new SearchIndexEntry(_referenceSearchTestParam, new ReferenceSearchValue(referenceKind, _baseUri, CompartmentDefinitionManager.CompartmentTypeToResourceType(compartmentType.ToString()).ToString(), expectedResourceId)),
                new SearchIndexEntry(_nonReferenceReferenceSearchTestParam, new StringSearchValue("aadsdas")),
            };
            CompartmentIndices compartmentIndices = _compartmentIndexer.Extract(resourceType.ToString(), searchIndexEntries);

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
            _compartmentManager.TryGetSearchParams(resourceType.ToString(), compartmentType, out compParams)
                .Returns(x =>
                {
                    x[2] = new HashSet<string> { _referenceSearchTestParam.Name };
                    return true;
                });
            var searchIndexEntries = new List<SearchIndexEntry>
            {
                new SearchIndexEntry(_referenceSearchTestParam, new ReferenceSearchValue(referenceKind, null, null, expectedResourceId)),
            };
            CompartmentIndices compartmentIndices = _compartmentIndexer.Extract(resourceType.ToString(), searchIndexEntries);

            Assert.Null(GetResourceIdsForCompartmentType(compartmentType, compartmentIndices));
        }

        [Theory]
        [InlineData(CompartmentType.Patient, CompartmentType.Encounter, CompartmentType.RelatedPerson)]
        [InlineData(CompartmentType.RelatedPerson, CompartmentType.Practitioner, CompartmentType.Patient)]
        public void GivenSearchIndicesWithMultipleReferenceSearchParams_WhenExtracted_ThenCorrectIndicesExtracted(CompartmentType expectedCompartmentType1, CompartmentType expectedCompartmentType2, CompartmentType expectedCompartmentType3)
        {
            HashSet<string> compParams = null;
            string expectedCareTeamResource = ResourceType.CareTeam.ToString();

            // Setup the compartment search definitions.
            _compartmentManager.TryGetSearchParams(expectedCareTeamResource, expectedCompartmentType1, out compParams)
                .Returns(x =>
                {
                    x[2] = new HashSet<string> { _referenceSearchTestParam.Name };
                    return true;
                });
            _compartmentManager.TryGetSearchParams(expectedCareTeamResource, expectedCompartmentType2, out compParams)
                .Returns(x =>
                {
                    x[2] = new HashSet<string> { _anotherReferenceSearchTestParam.Name };
                    return true;
                });
            _compartmentManager.TryGetSearchParams(expectedCareTeamResource, expectedCompartmentType3, out compParams)
                .Returns(x =>
                {
                    x[2] = new HashSet<string> { _yetAnotherReferenceSearchTestParam.Name };
                    return true;
                });
            const string ExpectedResourceIdR1 = "r1";
            const string ExpectedResourceIdR2 = "r2";
            const string ExpectedResourceIdR3 = "r3";

            // Setup multiple reference search parameters with expected resource ids for the compartments.
            var searchIndexEntries = new List<SearchIndexEntry>
            {
                new SearchIndexEntry(_referenceSearchTestParam, new ReferenceSearchValue(ReferenceKind.InternalOrExternal, _baseUri, CompartmentDefinitionManager.CompartmentTypeToResourceType(expectedCompartmentType1.ToString()).ToString(), ExpectedResourceIdR1)),
                new SearchIndexEntry(_anotherReferenceSearchTestParam, new ReferenceSearchValue(ReferenceKind.Internal, _baseUri, CompartmentDefinitionManager.CompartmentTypeToResourceType(expectedCompartmentType2.ToString()).ToString(), ExpectedResourceIdR2)),
                new SearchIndexEntry(_yetAnotherReferenceSearchTestParam, new ReferenceSearchValue(ReferenceKind.External, _baseUri, CompartmentDefinitionManager.CompartmentTypeToResourceType(expectedCompartmentType3.ToString()).ToString(), ExpectedResourceIdR3)),
            };

            CompartmentIndices compartmentIndices = _compartmentIndexer.Extract(expectedCareTeamResource, searchIndexEntries);

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
