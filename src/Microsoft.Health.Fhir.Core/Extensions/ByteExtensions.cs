// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Text;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Extensions
{
    public static class ByteExtensions
    {
        /// <summary>
        /// Encodes a Byte array to Base64 safely, this can be used in URLs.
        /// </summary>
        /// <param name="bytes">The bytes to encode.</param>
        /// <returns>An encoded string that's safe to be used in URLs.</returns>
        public static string ToSafeBase64(this byte[] bytes)
        {
            EnsureArg.IsNotNull(bytes, nameof(bytes));

            string base64 = Convert.ToBase64String(bytes);

            StringBuilder sb = new StringBuilder(base64.TrimEnd('='));

            sb.Replace('+', '-').Replace('/', '_');

            return sb.ToString();
        }
    }
}
