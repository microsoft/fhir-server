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
        public static string GetDatabaseName(this IFhirRequestContext fhirRequestContext)
        {
            if (fhirRequestContext.Partition == null)
            {
                throw new InvalidOperationException("Partition is not set in the request context.");
            }

            if (fhirRequestContext.Partition is not PartitionInfo partitionInfo)
            {
                throw new InvalidOperationException("Partition is not of type IPartitionInfo.");
            }

            if (string.IsNullOrEmpty(partitionInfo.DatabaseName))
            {
                throw new InvalidOperationException("Database name is not set in the partition.");
            }

            return partitionInfo.DatabaseName;
        }

        public static SqlObjectNameFormatter GetDatabaseNameFormatter(this IFhirRequestContext fhirRequestContext)
        {
            string databaseName = fhirRequestContext.GetDatabaseName();
            return new SqlObjectNameFormatter(databaseName);
        }
    }
}
