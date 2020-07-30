// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Reflection;
using EnumLiteralAttribute = Hl7.Fhir.Utility.EnumLiteralAttribute;

namespace Microsoft.Health.Fhir.Core.Extensions
{
    public static class EnumExtensions
    {
        public static T GetValueByEnumLiteral<T>(this string value)
            where T : Enum
        {
            FieldInfo val = typeof(T).GetFields()
                .FirstOrDefault(x => x.GetCustomAttributes().OfType<EnumLiteralAttribute>().Any(y => y.Literal == value));

            if (val != null)
            {
                return (T)val.GetRawConstantValue();
            }

            return default(T);
        }

        /// <summary>
        /// Converts the passed in enum to a camel case string, by lower casing the first character.
        /// </summary>
        /// <param name="e">Input Enum</param>
        /// <typeparam name="T">Enum type</typeparam>
        /// <returns>Camel cased string</returns>
        public static string ToCamelCaseString<T>(this T e)
            where T : Enum
        {
            var s = e.ToString();

            if (!string.IsNullOrEmpty(s))
            {
                return char.ToLowerInvariant(s[0]) + s.Substring(1);
            }

            return s;
        }
    }
}
