// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Text;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;

namespace Microsoft.Health.Fhir.SqlServer.Features.Partitions
{
    public static class FhirRequestContextExtensions
    {
        public static string GetDatabase(this IFhirRequestContext fhirRequestContext) => fhirRequestContext?.Partition.GetDatabase();

        public static SqlObjectNameFormatter GetDatabaseNameFormatter(this IFhirRequestContext fhirRequestContext)
        {
            string databaseName = fhirRequestContext.GetDatabase();
            return new SqlObjectNameFormatter(databaseName);
        }
    }
}
