// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections;
using System.Collections.Generic;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence;

public class SqlServerDataStoreValues : IEnumerable<object[]>
{
    public IEnumerator<object[]> GetEnumerator()
    {
        // SqlServerBlobEnabled will be included once the read path is implemented
        // yield return new object[] { DataStore.SqlServerBlobEnabled };
        yield return new object[] { DataStore.SqlServerBlobDisabled };
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
