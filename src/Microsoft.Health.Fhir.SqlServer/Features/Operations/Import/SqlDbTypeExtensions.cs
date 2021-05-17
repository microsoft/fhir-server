// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;

namespace Microsoft.Health.Fhir.SqlServer.Features.Operations.Import
{
    public static class SqlDbTypeExtensions
    {
        /// <summary>
        /// Mapping between sql db type to c# paramitive type
        /// </summary>
        internal static readonly Dictionary<SqlDbType, Type> EquivalentSystemType = new Dictionary<SqlDbType, Type>
        {
            { SqlDbType.BigInt, typeof(long) },
            { SqlDbType.Binary, typeof(byte[]) },
            { SqlDbType.Bit, typeof(bool) },
            { SqlDbType.Char, typeof(string) },
            { SqlDbType.Date, typeof(DateTime) },
            { SqlDbType.DateTime, typeof(DateTime) },
            { SqlDbType.DateTime2, typeof(DateTime) },
            { SqlDbType.DateTimeOffset, typeof(DateTimeOffset) },
            { SqlDbType.Decimal, typeof(decimal) },
            { SqlDbType.Float, typeof(double) },
            { SqlDbType.Image, typeof(byte[]) },
            { SqlDbType.Int, typeof(int) },
            { SqlDbType.Money, typeof(decimal) },
            { SqlDbType.NChar, typeof(string) },
            { SqlDbType.NVarChar, typeof(string) },
            { SqlDbType.Real, typeof(float) },
            { SqlDbType.SmallDateTime, typeof(DateTime) },
            { SqlDbType.SmallInt, typeof(short) },
            { SqlDbType.SmallMoney, typeof(decimal) },
            { SqlDbType.Time, typeof(TimeSpan) }, // SQL2008+
            { SqlDbType.TinyInt, typeof(byte) },
            { SqlDbType.UniqueIdentifier, typeof(Guid) },
            { SqlDbType.VarBinary, typeof(byte[]) },
            { SqlDbType.VarChar, typeof(string) },
        };

        public static Type GetGeneralType(this SqlDbType type)
        {
            return EquivalentSystemType[type];
        }
    }
}
