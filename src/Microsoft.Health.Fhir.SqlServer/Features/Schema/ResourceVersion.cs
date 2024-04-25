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
        private static bool versionIsLong = true;

        public static int GetVersion(SqlDataReader reader, int index)
        {
            int version = 0;
            bool retry = true;

            while (retry)
            {
                try
                {
                    version = versionIsLong ? (int)reader.GetInt64(index) : reader.GetInt32(index);
                    retry = false;
                }
                catch (InvalidCastException)
                {
                    versionIsLong = !versionIsLong;
                }
            }

            return version;
        }
    }
}
