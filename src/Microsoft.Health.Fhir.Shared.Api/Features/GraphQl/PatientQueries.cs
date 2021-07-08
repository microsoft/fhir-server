// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using HotChocolate.Types;
using Microsoft.Health.Fhir.Api.Features.GraphQl.DataLoader;

namespace Microsoft.Health.Fhir.Shared.Api.Features.GraphQl
{
    [ExtendObjectType("Query")]
#pragma warning disable CA1052 // Static holder types should be Static or NotInheritable
    public class PatientQueries
#pragma warning restore CA1052 // Static holder types should be Static or NotInheritable
    {
#pragma warning disable CA1822 // Mark members as static
        public Task<IEnumerable<Patient>> GetPatients(
            PatientByIdDataLoader patientById,
            CancellationToken cancellationToken) =>
            patientById.GetAllPatients(cancellationToken);

        public Task<Patient> GetPatientByIdAsync(
            string id,
            PatientByIdDataLoader patientById,
            CancellationToken cancellationToken) => patientById.LoadAsync(id, cancellationToken);
#pragma warning restore CA1822 // Mark members as static
    }
}
