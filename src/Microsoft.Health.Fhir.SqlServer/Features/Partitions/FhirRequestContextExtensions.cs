// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;

namespace Microsoft.Health.Fhir.SqlServer.Features.Partitions
{
    public static class FhirRequestContextExtensions
    {
        public static string GetDatabase(this IFhirRequestContext fhirRequestContext)
        {
            if (fhirRequestContext?.Partition == null)
            {
                return null;
            }

            if (fhirRequestContext.Partition is not SqlPartitionInfo partitionInfo)
            {
                throw new InvalidOperationException("Partition is not of type SqlPartitionInfo.");
            }

            return partitionInfo.Database;
        }

        public static SqlObjectNameFormatter GetDatabaseNameFormatter(this IFhirRequestContext fhirRequestContext)
        {
            string databaseName = fhirRequestContext.GetDatabase();
            return new SqlObjectNameFormatter(databaseName);
        }
    }
}
