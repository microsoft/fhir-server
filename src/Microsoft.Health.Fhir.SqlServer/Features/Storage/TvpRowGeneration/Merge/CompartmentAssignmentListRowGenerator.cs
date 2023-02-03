// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class CompartmentAssignmentListRowGenerator : ITableValuedParameterRowGenerator<IReadOnlyList<MergeResourceWrapper>, CompartmentAssignmentListRow>
    {
        private readonly ISqlServerFhirModel _model;
        private readonly SearchParameterToSearchValueTypeMap _searchParameterTypeMap;
        private bool _initialized;
        private byte _patientCompartmentId;
        private byte _encounterCompartmentId;
        private byte _relatedPersonCompartmentId;
        private byte _practitionerCompartmentId;
        private byte _deviceCompartmentId;

        public CompartmentAssignmentListRowGenerator(ISqlServerFhirModel model, SearchParameterToSearchValueTypeMap searchParameterTypeMap)
        {
            EnsureArg.IsNotNull(model, nameof(model));
            EnsureArg.IsNotNull(searchParameterTypeMap, nameof(searchParameterTypeMap));

            _model = model;
            _searchParameterTypeMap = searchParameterTypeMap;
        }

        public IEnumerable<CompartmentAssignmentListRow> GenerateRows(IReadOnlyList<MergeResourceWrapper> resources)
        {
            EnsureInitialized();

            foreach (var merge in resources.Where(_ => !_.ResourceWrapper.IsHistory)) // only current
            {
                var resource = merge.ResourceWrapper;
                var typeId = _model.GetResourceTypeId(resource.ResourceTypeName);

                var resourceMetadata = new ResourceMetadata(
                    resource.CompartmentIndices,
                    resource.SearchIndices?.ToLookup(e => _searchParameterTypeMap.GetSearchValueType(e)),
                    resource.LastModifiedClaims);

                CompartmentIndices compartments = resourceMetadata.Compartments;
                if (compartments == null)
                {
                    continue;
                }

                var results = new HashSet<CompartmentAssignmentListRow>();

                if (compartments.PatientCompartmentEntry != null)
                {
                    foreach (var entry in compartments.PatientCompartmentEntry)
                    {
                        var row = new CompartmentAssignmentListRow(typeId, merge.ResourceSurrogateId, _patientCompartmentId, entry);
                        if (results.Add(row))
                        {
                            yield return row;
                        }
                    }
                }

                if (compartments.EncounterCompartmentEntry != null)
                {
                    foreach (var entry in compartments.EncounterCompartmentEntry)
                    {
                        var row = new CompartmentAssignmentListRow(typeId, merge.ResourceSurrogateId, _encounterCompartmentId, entry);
                        if (results.Add(row))
                        {
                            yield return row;
                        }
                    }
                }

                if (compartments.RelatedPersonCompartmentEntry != null)
                {
                    foreach (var entry in compartments.RelatedPersonCompartmentEntry)
                    {
                        var row = new CompartmentAssignmentListRow(typeId, merge.ResourceSurrogateId, _relatedPersonCompartmentId, entry);
                        if (results.Add(row))
                        {
                            yield return row;
                        }
                    }
                }

                if (compartments.PractitionerCompartmentEntry != null)
                {
                    foreach (var entry in compartments.PractitionerCompartmentEntry)
                    {
                        var row = new CompartmentAssignmentListRow(typeId, merge.ResourceSurrogateId, _practitionerCompartmentId, entry);
                        if (results.Add(row))
                        {
                            yield return row;
                        }
                    }
                }

                if (compartments.DeviceCompartmentEntry != null)
                {
                    foreach (var entry in compartments.DeviceCompartmentEntry)
                    {
                        var row = new CompartmentAssignmentListRow(typeId, merge.ResourceSurrogateId, _deviceCompartmentId, entry);
                        if (results.Add(row))
                        {
                            yield return row;
                        }
                    }
                }
            }
        }

        private void EnsureInitialized()
        {
            if (Volatile.Read(ref _initialized))
            {
                return;
            }

            _patientCompartmentId = _model.GetCompartmentTypeId(KnownCompartmentTypes.Patient);
            _encounterCompartmentId = _model.GetCompartmentTypeId(KnownCompartmentTypes.Encounter);
            _relatedPersonCompartmentId = _model.GetCompartmentTypeId(KnownCompartmentTypes.RelatedPerson);
            _practitionerCompartmentId = _model.GetCompartmentTypeId(KnownCompartmentTypes.Practitioner);
            _deviceCompartmentId = _model.GetCompartmentTypeId(KnownCompartmentTypes.Device);

            Volatile.Write(ref _initialized, true);
        }
    }
}
