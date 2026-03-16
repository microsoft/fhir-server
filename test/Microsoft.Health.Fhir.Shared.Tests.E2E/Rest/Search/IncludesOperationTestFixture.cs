// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.SqlServer.Management.Smo;
using NSubstitute.Core;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    public class IncludesOperationTestFixture : HttpIntegrationTestFixture
    {
        // 8 Patients (matched resource type)
        // - patient0 ~ patient7
        // 5 Groups
        // - group0: patient0, patient1, patient2, patient3
        // - group1: patient0, patient4, patient5
        // - group2: patient1, patient2, patient6
        // - group3: patient2, patient4, patient7
        // - group4: patient4, patient6
        // 3 Organizations
        // - organization0: patient0
        // - organization1: patient2
        // - organization2: patient4
        // 4 Practitioners
        // - practitioner0: patient0, patient1, patient2
        // - practitioner1: patient3, patient4
        // - practitioner2: patient5
        // - practitioner3: patient6, patient7
        // 10 Observations
        // - observation0: patient0, practitioner0, oranization0
        // - observation1: patient1, practitioner0
        // - observation2: patient1, oranization1
        // - observation3: patient2, practitioner0, oranization1
        // - observation4: patient3, practitioner1
        // - observation5: patient4, practitioner1, oranization2
        // - observation6: patient4, oranization2
        // - observation7: patient5, practitioner2
        // - observation8: patient6, practitioner3
        // - observation9: patient7, practitioner3
        // 10 DiagnosticReports
        // - report0: patient0, observation0
        // - report1: patient1, observation1
        // - report2: patient1, observation2
        // - report3: patient2, observation3
        // - report4: patient3, observation4
        // - report5: patient4, observation5
        // - report6: patient4, observation6
        // - report7: patient5, observation7
        // - report8: patient6, observation8
        // - report9: patient7, observation9
        // 4 Medications
        // - medication0 ~ medication3
        // 10 MedicationRequests
        // - request0: patient0, practitioner0, medication0
        // - request1: patient0, practitioner0, medication1
        // - request2: patient0, practitioner0, medication2
        // - request3: patient1, practitioner0, medication1
        // - request4: patient2, practitioner0, medication2
        // - request5: patient2, practitioner0, medication3
        // - request6: patient4, practitioner1, medication2
        // - request7: patient5, practitioner2, medication3
        // - request8: patient6, practitioner3, medication1
        // - request9: patient6, practitioner3, medication3
        // 10 MedicationDispenses
        // - dispense0: patient0, request0, medication0
        // - dispense1: patient0, request1, medication1
        // - dispense2: patient0, request2, medication2
        // - dispense3: patient1, request3, medication1
        // - dispense4: patient2, request4, medication2
        // - dispense5: patient2, request5, medication3
        // - dispense6: patient4, request6, medication2
        // - dispense7: patient5, request7, medication3
        // - dispense8: patient6, request8, medication1
        // - dispense9: patient6, request9, medication3
        private const string ResourceFileName = "Includes-TestResources";

        private static readonly string[] _knownRelatedResourceTypes =
        {
            KnownResourceTypes.DiagnosticReport,
            KnownResourceTypes.Group,
            KnownResourceTypes.MedicationDispense,
            KnownResourceTypes.MedicationRequest,
            KnownResourceTypes.Organization,
            KnownResourceTypes.Observation,
            KnownResourceTypes.Practitioner,
        };

        private readonly List<Resource> _patientResources;
        private readonly List<Resource> _relatedResources;
        private readonly Dictionary<string, IList<Resource>> _relatedResourcesByResourceType;
        private readonly string _tag;

        public IncludesOperationTestFixture(
            DataStore dataStore,
            Format format,
            TestFhirServerFactory testFhirServerFactory)
            : base(dataStore, format, testFhirServerFactory)
        {
            _patientResources = new List<Resource>();
            _relatedResources = new List<Resource>();
            _relatedResourcesByResourceType = new Dictionary<string, IList<Resource>>(StringComparer.OrdinalIgnoreCase);
            _tag = $"includestest-{DateTime.UtcNow.Ticks}";
        }

        public string[] KnownRelatedResourceTypes => _knownRelatedResourceTypes;

        public IList<Resource> PatientResources => _patientResources;

        public IList<Resource> RelatedResources => _relatedResources;

        public string Tag => _tag;

        protected override async Task OnInitializedAsync()
        {
            await base.OnInitializedAsync();
            var bundle = TagResources((Bundle)Samples.GetJsonSample(ResourceFileName).ToPoco());
            var response = await TestFhirClient.PostBundleAsync(bundle);
            _patientResources.AddRange(
                response.Resource.Entry
                    .Select(x => x.Resource)
                    .Where(x => x.TypeName.Equals(KnownResourceTypes.Patient, StringComparison.OrdinalIgnoreCase)));
            _relatedResources.AddRange(
                response.Resource.Entry
                    .Select(x => x.Resource)
                    .Where(x => !x.TypeName.Equals(KnownResourceTypes.Patient, StringComparison.OrdinalIgnoreCase)
                        && !x.TypeName.Equals(KnownResourceTypes.Medication, StringComparison.OrdinalIgnoreCase)));
            foreach (var resource in RelatedResources)
            {
                if (!_relatedResourcesByResourceType.TryGetValue(resource.TypeName, out _))
                {
                    _relatedResourcesByResourceType.Add(resource.TypeName, new List<Resource>());
                }

                _relatedResourcesByResourceType[resource.TypeName].Add(resource);
            }
        }

        public IDictionary<string, IList<Resource>> RelatedResourcesFor(
            IList<Resource> patientResources,
            string[] relatedResourceTypes)
        {
            var patientReferences = patientResources.Select(x => $"{x.TypeName}/{x.Id}").ToHashSet();
            var relatedResources = new Dictionary<string, IList<Resource>>(StringComparer.OrdinalIgnoreCase);
            foreach (var resourceType in relatedResourceTypes)
            {
                if (!relatedResources.TryGetValue(resourceType, out _) && _relatedResourcesByResourceType.TryGetValue(resourceType, out var relatedResourcesByResourceType))
                {
                    var resources = relatedResourcesByResourceType.Where(
                        x =>
                        {
                            switch (x.TypeName)
                            {
                                case KnownResourceTypes.DiagnosticReport:
                                    return patientReferences.Contains(((DiagnosticReport)x).Subject?.Reference);

                                case KnownResourceTypes.Group:
                                    return ((Group)x).Member.Any(y => patientReferences.Contains(y.Entity?.Reference));

                                case KnownResourceTypes.MedicationDispense:
                                    return patientReferences.Contains(((MedicationDispense)x).Subject?.Reference);

                                case KnownResourceTypes.MedicationRequest:
                                    return patientReferences.Contains(((MedicationRequest)x).Subject?.Reference);

                                case KnownResourceTypes.Observation:
                                    return patientReferences.Contains(((Observation)x).Subject?.Reference);

                                case KnownResourceTypes.Organization:
                                    return patientResources.Any(y => ((Patient)y).ManagingOrganization?.Reference?.Equals($"{x.TypeName}/{x.Id}") ?? false);

                                case KnownResourceTypes.Practitioner:
                                    return patientResources.Any(y => ((Patient)y).GeneralPractitioner.Any(z => z.Reference?.Equals($"{x.TypeName}/{x.Id}") ?? false));

                                default:
                                    throw new InvalidOperationException($"Unsupported resource type: {resourceType}");
                            }
                        });

                    if (resources.Any())
                    {
                        relatedResources.Add(resourceType, new List<Resource>(resources));
                    }
                }
            }

            return relatedResources;
        }

        private Resource TagResources(Bundle bundle)
        {
            foreach (var entry in bundle.Entry)
            {
                entry.Resource.Meta = new Meta
                {
                    Tag = new List<Coding>
                    {
                        new Coding("testTag", _tag),
                    },
                };
            }

            return bundle;
        }
    }
}
