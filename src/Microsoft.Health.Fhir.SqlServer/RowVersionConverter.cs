// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.SqlServer
{
    /// <summary>
    /// Enables converting the row version value read from the export job SQL table to a decimal string Etag stored in an export job record.
    /// </summary>
    internal static class RowVersionConverter
    {
        internal static byte[] GetVersionAsBytes(string versionAsDecimalString)
        {
            // The SQL rowversion data type is 8 bytes in length.
            var versionAsBytes = new byte[8];

            BitConverter.TryWriteBytes(versionAsBytes, int.Parse(versionAsDecimalString));

            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(versionAsBytes);
            }

            return versionAsBytes;
        }

        internal static string GetVersionAsDecimalString(byte[] versionAsBytes)
        {
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(versionAsBytes);
            }

            const int startIndex = 0;

            return BitConverter.ToInt32(versionAsBytes, startIndex).ToString();
        }
    }
}
