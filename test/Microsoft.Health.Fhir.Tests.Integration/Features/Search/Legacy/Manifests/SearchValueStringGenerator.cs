// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Tests.Integration.Features.Search.Legacy.Manifests
{
    public static class SearchValueStringGenerator
    {
        public static string GenerateNumber(decimal decimalValue) =>
            decimalValue.ToString(CultureInfo.InvariantCulture);

        public static string GenerateQuantityString(Quantity quantity) =>
            $"{quantity.Value}|{quantity.System}|{quantity.Code}";

        public static string GenerateString(string stringValue) =>
            stringValue;

        public static string GenerateTokenString(Coding coding)
        {
            return $"{coding.System}|{coding.Code}";
        }
    }
}
