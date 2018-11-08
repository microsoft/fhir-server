// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Hl7.Fhir.Model;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    public class DateSearchTestFixture : SearchTestFixture
    {
        public IReadOnlyList<Patient> Patients { get; private set; }

        protected override async Task InitializeInternalAsync()
        {
            Patients = await CreateAsync<Patient>(
                p => p.BirthDate = "1979-12-31",                // 1979-12-31T00:00:00.0000000 <-> 1979-12-31T23:59:59.9999999
                p => p.BirthDate = "1980",                      // 1980-01-01T00:00:00.0000000 <-> 1980-12-31T23:59:59.9999999
                p => p.BirthDate = "1980-05",                   // 1980-05-01T00:00:00.0000000 <-> 1980-05-31T23:59:59.9999999
                p => p.BirthDate = "1980-05-11",                // 1980-05-11T00:00:00.0000000 <-> 1980-05-11T23:59:59.9999999
                p => p.BirthDate = "1980-05-11T16:32",          // 1980-05-11T16:32:00.0000000 <-> 1980-05-11T16:32:59.9999999
                p => p.BirthDate = "1980-05-11T16:32:15",       // 1980-05-11T16:32:15.0000000 <-> 1980-05-11T16:32:15.9999999
                p => p.BirthDate = "1980-05-11T16:32:15.500",   // 1980-05-11T16:32:15.5000000 <-> 1980-05-11T16:32:15.5000000
                p => p.BirthDate = "1981-01-01");        // 1981-01-01T00:00:00.0000000 <-> 1981-12-31T23:59:59.9999999
        }
    }
}
