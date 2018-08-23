// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Core.Extensions
{
    public static class DecimalExtensions
    {
        /// <summary>
        /// Get a decimal for use as a precision modifier of 1/2 the next decimal point.
        /// Given a value of 1 the decimal .5 is returned. Given a value of 100.00 the decimal .005 is returned.
        /// </summary>
        /// <param name="d">The decimal to modify.</param>
        /// <returns>The value to modify the decimal by.</returns>
        public static decimal GetPrescisionModifier(this decimal d)
        {
            // http://csharpindepth.com/Articles/General/Decimal.aspx
            // Exponents are stored in the third byte of the fourth integer
            var digitsBehindDecimal = BitConverter.GetBytes(decimal.GetBits(d)[3])[2];

            // Exponents are limited to 28 decimal digits in most cases, so we can't modify the value further
            if (digitsBehindDecimal >= 28)
            {
                return 0;
            }

            // We want to create a decimal value that has the value of 5 one decimal digit further from 0
            return new decimal(5, 0, 0, false, (byte)(digitsBehindDecimal + 1));
        }
    }
}
