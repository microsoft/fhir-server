// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Data.SqlTypes;

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

            try
            {
                SqlDecimal.ConvertToPrecScale(new SqlDecimal(value), precision, scale);
            }
            catch (Exception ex) when (ex is SqlTruncateException || ex is OverflowException)
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
    }
}
