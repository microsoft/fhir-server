// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using NSubstitute;

namespace Microsoft.Health.Fhir.Shared.Core.UnitTests.Features.Persistence.Orchestration
{
    public static class BundleTestsCommonFunctions
    {
        private static readonly FhirJsonSerializer JsonSerializer;
        private static readonly ResourceDeserializer ResourceDeserializer;

        static BundleTestsCommonFunctions()
        {
            JsonSerializer = new FhirJsonSerializer();
            ResourceDeserializer = Deserializers.ResourceDeserializer;
        }

        public static DomainResource GetSamplePatient(Guid id)
        {
            Patient patient = new Patient();
            patient.Id = id.ToString();
            patient.Meta = new Meta() { Profile = new string[] { "http://hl7.org/fhir/us/core/StructureDefinition/us-core-encounter" } };
            patient.Identifier.Add(new Identifier()
            {
                Use = Identifier.IdentifierUse.Official,
                System = "https://github.com/synthetichealth/synthea",
                Value = patient.Id,
            });
            patient.Name.Add(new HumanName()
            {
                Use = HumanName.NameUse.Official,
                Family = "Foo",
                Given = new string[] { $"Bar {patient.Id}" },
            });
            patient.Telecom.Add(new ContactPoint()
            {
                System = ContactPoint.ContactPointSystem.Phone,
                Value = "555-106-9045",
                Use = ContactPoint.ContactPointUse.Work,
            });
            patient.Gender = AdministrativeGender.Male;
            patient.BirthDate = "2012-02-05";
            patient.Address.Add(new Address()
            {
                City = "Redmond",
                State = "WA",
                Country = "USA",
                Line = new string[] { "426 Fadel Approach" },
            });

            return patient;
        }

        public static async Task<ResourceWrapper> GetResourceWrapperAsync(DomainResource resource)
        {
            var json = await JsonSerializer.SerializeToStringAsync(resource);

            var rawResource = new RawResource(json, FhirResourceFormat.Json, isMetaSet: false);

            var resourceRequest = Substitute.For<ResourceRequest>();

            var compartmentIndices = Substitute.For<CompartmentIndices>();

            var resourceElement = ResourceDeserializer.DeserializeRaw(rawResource, "v1", DateTimeOffset.UtcNow);

            var wrapper = new ResourceWrapper(
                resourceElement,
                rawResource,
                resourceRequest,
                false,
                new List<SearchIndexEntry>(),
                compartmentIndices,
                new List<KeyValuePair<string, string>>(),
                "hash");

            return wrapper;
        }

        public static async Task<ResourceWrapperOperation> GetResourceWrapperOperationAsync(DomainResource resource, Guid bundleOperationId)
        {
            ResourceWrapper wrapper = await GetResourceWrapperAsync(resource);
            return new ResourceWrapperOperation(wrapper, true, true, null, false, bundleOperationId);
        }
    }
}
