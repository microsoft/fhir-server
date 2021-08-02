// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Persistence;

namespace Microsoft.Health.Fhir.Core.Features.Search
{
    public sealed class ContinuationTokenConverter
    {
        public static string Decode(string encodedContinuationToken)
        {
            try
            {
                return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(encodedContinuationToken));
            }
            catch (FormatException)
            {
                throw new BadRequestException(Resources.InvalidContinuationToken);
            }
        }

        public static string Encode(string continuationToken)
        {
            EnsureArg.IsNotEmptyOrWhiteSpace(continuationToken);
            return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(continuationToken));
        }
    }
}
