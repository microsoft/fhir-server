// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;

namespace Microsoft.Health.Fhir.Core.Features.Search.Converters
{
    internal static class CodingExtensions
    {
        internal static TokenSearchValue ToTokenSearchValue(this Coding coding)
        {
            EnsureArg.IsNotNull(coding, nameof(coding));

            if (!string.IsNullOrWhiteSpace(coding.System) ||
                !string.IsNullOrWhiteSpace(coding.Code) ||
                !string.IsNullOrWhiteSpace(coding.Display))
            {
                return new TokenSearchValue(coding.System, coding.Code, coding.Display);
            }

            return null;
        }
    }
}
