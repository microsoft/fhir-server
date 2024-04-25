// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Data.SqlClient;

namespace Microsoft.Health.Fhir.SqlServer.Features.Schema
{
    public static class ResourceVersion
    {
        public static int GetVersion(SqlDataReader reader, int index)
        {
            if (reader.GetFieldType(index) == typeof(long))
            {
                return (int)reader.GetInt64(index);
            }

            return reader.GetInt32(index);
        }
    }
}
