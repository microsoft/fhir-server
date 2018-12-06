// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Hl7.Fhir.Model;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    public class UriSearchTestFixture : SearchTestFixture
    {
        public IReadOnlyList<ValueSet> ValueSets { get; private set; }

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();

            ValueSets = await CreateResourcesAsync<ValueSet>(
                vs => vs.Identifier,
                vs => AddValueSet(vs, "http://somewhere.com/test/system"),
                vs => AddValueSet(vs, "urn://localhost/test"));

            void AddValueSet(ValueSet vs, string url)
            {
                vs.Status = PublicationStatus.Active;
                vs.Url = url;
            }
        }
    }
}
