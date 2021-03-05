// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using System.Linq;
using System.Text;

namespace Microsoft.Health.Fhir.CosmosDb
{
    internal static class StringExtensions
    {
        /// <summary>
        /// Removes accent characters from a string.
        /// </summary>
        /// <param name="str">Input string</param>
        /// <returns>Normalized string</returns>
        public static string NormalizeAndRemoveAccents(this string str)
        {
            var normalized = str
                .Normalize(NormalizationForm.FormKD) // Split out accents, sub/super text.
                .ToCharArray()
                .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) // Removes the accent
                .ToArray();

            return new string(normalized);
        }
    }
}
