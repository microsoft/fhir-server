// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading.Tasks;
using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Shared.Web
{
    public class Query
    {
        public async Task<Patient> GetPatient(
            string id,
            PatientBatchDataLoader dataLoader)
            => await dataLoader.LoadAsync(id);
    }

    /*public class Query
    {
        public Patient Patient(string id) => new Patient { Id = id, Active = true };
    }*/
}
