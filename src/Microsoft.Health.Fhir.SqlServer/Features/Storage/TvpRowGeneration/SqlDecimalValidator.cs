// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Globalization;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal static class SqlDecimalValidator
    {
        public const int DefaultPrecision = 36;
        public const int DefaultScale = 18;

        public static decimal Validate(decimal value, int precision = DefaultPrecision, int scale = DefaultScale, string parameterName = "value")
        {
            if (precision <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(precision), "Precision must be greater than zero.");
            }

            if (scale < 0 || scale > precision)
            {
                throw new ArgumentOutOfRangeException(nameof(scale), $"Scale must be between zero and {precision}.");
            }

            int integralDigits = GetIntegralDigitCount(value);
            int fractionalDigits = GetFractionalDigitCount(value);

            if (fractionalDigits > scale)
            {
                decimal roundedValue = decimal.Round(value, scale);
                if (roundedValue != value)
                {
                    throw new SqlDecimalValueOutOfRangeException(parameterName, $"The decimal value exceeds the allowed scale ({scale}) for the target SQL decimal type.");
                }
            }

            if (integralDigits > precision - scale)
            {
                throw new SqlDecimalValueOutOfRangeException(parameterName, $"The decimal value exceeds the allowed precision ({precision}) and scale ({scale}) for the target SQL decimal type.");
            }

            return value;
        }

        public static decimal? Validate(decimal? value, int precision = DefaultPrecision, int scale = DefaultScale, string parameterName = "value")
        {
            if (!value.HasValue)
            {
                return value;
            }

            return Validate(value.Value, precision, scale, parameterName);
        }

        private static int GetIntegralDigitCount(decimal value)
        {
            decimal absValue = decimal.Abs(value);
            if (absValue == 0m)
            {
                return 1;
            }

            var text = absValue.ToString(CultureInfo.InvariantCulture);
            int decimalIndex = text.IndexOf('.', StringComparison.Ordinal);
            if (decimalIndex < 0)
            {
                return text.Length;
            }

            int integralLength = decimalIndex;
            if (integralLength == 0)
            {
                return 0;
            }

            return integralLength;
        }

        private static int GetFractionalDigitCount(decimal value)
        {
            int[] bits = decimal.GetBits(value);
            return (bits[3] >> 16) & 0xFF;
        }
    }
}
