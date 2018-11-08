// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Hl7.Fhir.Model;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    public class ReferenceSearchTestFixture : SearchTestFixture
    {
        public IReadOnlyList<Patient> Patients { get; private set; }

        protected override async Task InitializeInternalAsync()
        {
            Patients = await CreateAsync<Patient>(
                p => p.ManagingOrganization = new ResourceReference("Organization/123"),
                p => p.ManagingOrganization = new ResourceReference("Organization/abc"));
        }
    }
}
