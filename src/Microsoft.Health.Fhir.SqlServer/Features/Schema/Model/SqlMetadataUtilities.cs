// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Data;
using EnsureThat;
using Microsoft.SqlServer.Server;

namespace Microsoft.Health.Fhir.SqlServer.Features.Schema.Model
{
    internal static class SqlMetadataUtilities
    {
        internal static decimal GetMinValueForDecimalColumn(SqlMetaData columnMetaData)
        {
            return GetMinOrMaxSqlDecimalValueForColumn(columnMetaData, min: true);
        }

        internal static decimal GetMaxValueForDecimalColumn(SqlMetaData columnMetaData)
        {
            return GetMinOrMaxSqlDecimalValueForColumn(columnMetaData, min: false);
        }

        private static decimal GetMinOrMaxSqlDecimalValueForColumn(SqlMetaData columnMetadata, bool min)
        {
            EnsureArg.IsNotNull(columnMetadata, nameof(columnMetadata));
            EnsureArg.Is((int)SqlDbType.Decimal, (int)columnMetadata.SqlDbType, nameof(columnMetadata));

            var val = decimal.Parse($"{new string('9', columnMetadata.Precision - columnMetadata.Scale)}.{new string('9', columnMetadata.Scale)}");
            return min ? -val : val;
        }
    }
}
