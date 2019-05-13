﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading;
using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class CompartmentAssignmentRowGenerator : ITableValuedParameterRowGenerator<ResourceWrapper, V1.CompartmentAssignmentTableTypeRow>
    {
        private readonly SqlServerFhirModel _model;
        private bool _initialized;
        private byte _patientCompartmentId;
        private byte _encounterCompartmentId;
        private byte _relatedPersonCompartmentId;
        private byte _practitionerCompartmentId;
        private byte _deviceCompartmentId;

        public CompartmentAssignmentRowGenerator(SqlServerFhirModel model)
        {
            EnsureArg.IsNotNull(model, nameof(model));
            _model = model;
        }

        public IEnumerable<V1.CompartmentAssignmentTableTypeRow> GenerateRows(ResourceWrapper resource)
        {
            EnsureInitialized();

            var compartments = resource.CompartmentIndices;
            if (compartments == null)
            {
                yield break;
            }

            if (compartments.PatientCompartmentEntry != null)
            {
                foreach (var entry in compartments.PatientCompartmentEntry)
                {
                    yield return new V1.CompartmentAssignmentTableTypeRow(_patientCompartmentId, entry);
                }
            }

            if (compartments.EncounterCompartmentEntry != null)
            {
                foreach (var entry in compartments.EncounterCompartmentEntry)
                {
                    yield return new V1.CompartmentAssignmentTableTypeRow(_encounterCompartmentId, entry);
                }
            }

            if (compartments.RelatedPersonCompartmentEntry != null)
            {
                foreach (var entry in compartments.RelatedPersonCompartmentEntry)
                {
                    yield return new V1.CompartmentAssignmentTableTypeRow(_relatedPersonCompartmentId, entry);
                }
            }

            if (compartments.PractitionerCompartmentEntry != null)
            {
                foreach (var entry in compartments.PractitionerCompartmentEntry)
                {
                    yield return new V1.CompartmentAssignmentTableTypeRow(_practitionerCompartmentId, entry);
                }
            }

            if (compartments.DeviceCompartmentEntry != null)
            {
                foreach (var entry in compartments.DeviceCompartmentEntry)
                {
                    yield return new V1.CompartmentAssignmentTableTypeRow(_deviceCompartmentId, entry);
                }
            }
        }

        private void EnsureInitialized()
        {
            if (Volatile.Read(ref _initialized))
            {
                return;
            }

            _patientCompartmentId = _model.GetCompartmentId(CompartmentType.Patient);
            _encounterCompartmentId = _model.GetCompartmentId(CompartmentType.Encounter);
            _relatedPersonCompartmentId = _model.GetCompartmentId(CompartmentType.RelatedPerson);
            _practitionerCompartmentId = _model.GetCompartmentId(CompartmentType.Practitioner);
            _deviceCompartmentId = _model.GetCompartmentId(CompartmentType.Device);

            Volatile.Write(ref _initialized, true);
        }
    }
}
