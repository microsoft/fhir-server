// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Persistence.Orchestration;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using NSubstitute;
using NSubstitute.Core;

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

        public static IBundleOrchestrator GetBundleOrchestrator(bool isBundleOrchestratorEnabled = true)
        {
            return new BundleOrchestrator(GetBundleConfiguration(isBundleOrchestratorEnabled), NullLogger<BundleOrchestrator>.Instance);
        }

        public static IFhirDataStore GetSubstituteForIFhirDataStore()
        {
            var dataStore = Substitute.For<IFhirDataStore>();

            /// In this parg of the code I'm replacing the default behavior of <see cref="Substitute"/> for the method 'MergeAsync'.
            /// I've added a validation to Bundle Orchestrator Operation to avoid null instances of DataStoreOperationOutcome.
            /// To make the tests operating as expected, I've overrided the default behavior of <see cref="Substitute"/> and set the mock
            /// version of 'MergeAsync' to return some basic values for tests.
            dataStore.MergeAsync(Arg.Any<IReadOnlyList<ResourceWrapperOperation>>(), Arg.Any<CancellationToken>()).ReturnsForAnyArgs(MockMergeAsync);

            return dataStore;
        }

        public static IOptions<BundleConfiguration> GetBundleConfiguration(bool isBundleOrchestratorEnabled = true)
        {
            var bundleConfiguration = new BundleConfiguration();
            var bundleOptions = Substitute.For<IOptions<BundleConfiguration>>();

            bundleOptions.Value.Returns(bundleConfiguration);

            return bundleOptions;
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
            return new ResourceWrapperOperation(wrapper, true, true, null, requireETagOnUpdate: false, keepVersion: false, bundleOperationId: bundleOperationId);
        }

        private static Task<IDictionary<DataStoreOperationIdentifier, DataStoreOperationOutcome>> MockMergeAsync(CallInfo arg)
        {
            IReadOnlyList<ResourceWrapperOperation> operations = arg.Arg<IReadOnlyList<ResourceWrapperOperation>>();
            IDictionary<DataStoreOperationIdentifier, DataStoreOperationOutcome> outcomes = new Dictionary<DataStoreOperationIdentifier, DataStoreOperationOutcome>(operations.Count);

            foreach (ResourceWrapperOperation operation in operations)
            {
                outcomes.Add(operation.GetIdentifier(), new DataStoreOperationOutcome(outcome: null));
            }

            return System.Threading.Tasks.Task.FromResult(outcomes);
        }
    }
}
