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
    }
}
