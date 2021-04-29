// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class CompartmentAssignmentV1RowGenerator : ITableValuedParameterRowGenerator<ResourceMetadata, CompartmentAssignmentTableTypeV1Row>
    {
        private readonly ISqlServerFhirModel _model;
        private bool _initialized;
        private byte _patientCompartmentId;
        private byte _encounterCompartmentId;
        private byte _relatedPersonCompartmentId;
        private byte _practitionerCompartmentId;
        private byte _deviceCompartmentId;

        public CompartmentAssignmentV1RowGenerator(ISqlServerFhirModel model)
        {
            EnsureArg.IsNotNull(model, nameof(model));
            _model = model;
        }

        public IEnumerable<CompartmentAssignmentTableTypeV1Row> GenerateRows(ResourceMetadata resourceMetadata)
        {
            EnsureInitialized();

            var compartments = resourceMetadata.Compartments;
            if (compartments == null)
            {
                yield break;
            }

            if (compartments.PatientCompartmentEntry != null)
            {
                foreach (var entry in compartments.PatientCompartmentEntry)
                {
                    yield return new CompartmentAssignmentTableTypeV1Row(_patientCompartmentId, entry);
                }
            }

            if (compartments.EncounterCompartmentEntry != null)
            {
                foreach (var entry in compartments.EncounterCompartmentEntry)
                {
                    yield return new CompartmentAssignmentTableTypeV1Row(_encounterCompartmentId, entry);
                }
            }

            if (compartments.RelatedPersonCompartmentEntry != null)
            {
                foreach (var entry in compartments.RelatedPersonCompartmentEntry)
                {
                    yield return new CompartmentAssignmentTableTypeV1Row(_relatedPersonCompartmentId, entry);
                }
            }

            if (compartments.PractitionerCompartmentEntry != null)
            {
                foreach (var entry in compartments.PractitionerCompartmentEntry)
                {
                    yield return new CompartmentAssignmentTableTypeV1Row(_practitionerCompartmentId, entry);
                }
            }

            if (compartments.DeviceCompartmentEntry != null)
            {
                foreach (var entry in compartments.DeviceCompartmentEntry)
                {
                    yield return new CompartmentAssignmentTableTypeV1Row(_deviceCompartmentId, entry);
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
