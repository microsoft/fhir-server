// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Security.Cryptography;
using System.Text;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Extensions
{
    public static class StringExtensions
    {
        /// <summary>
        /// Computes SHA256 hash based of <paramref name="data"/>.
        /// </summary>
        /// <param name="data">The data to compute hash on.</param>
        /// <returns>The computed hash string.</returns>
        public static string ComputeHash(this string data)
        {
            EnsureArg.IsNotNull(data, nameof(data));

            using (var sha256 = new SHA256Managed())
            {
                var hashed = sha256.ComputeHash(Encoding.UTF8.GetBytes(data));
                return BitConverter.ToString(hashed).Replace("-", string.Empty, StringComparison.Ordinal);
            }
        }
    }
}
