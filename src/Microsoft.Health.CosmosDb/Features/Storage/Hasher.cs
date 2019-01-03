// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Security.Cryptography;
using System.Text;

namespace Microsoft.Health.CosmosDb.Features.Storage
{
    internal class Hasher
    {
        public static string ComputeHash(string data)
        {
            using (var sha256 = new SHA256Managed())
            {
                var hashed = sha256.ComputeHash(Encoding.UTF8.GetBytes(data));
                return BitConverter.ToString(hashed).Replace("-", string.Empty, StringComparison.Ordinal);
            }
        }
    }
}
