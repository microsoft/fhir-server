// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Xunit.Abstractions;

namespace Microsoft.Health.Extensions.Xunit
{
    public static class EnumHelper
    {
        public static SingleFlagEnum[][] ExpandEnumFlagsFromAttributeData(IAttributeInfo attributeInfo)
        {
            return attributeInfo
                .GetConstructorArguments()
                .Cast<Enum>()
                .Select(e => GetFlags(e).ToArray())
                .ToArray();
        }

        private static IEnumerable<SingleFlagEnum> GetFlags(Enum e)
        {
            if (e is null)
            {
                yield break;
            }

            var eLong = Convert.ToInt64(e);

            foreach (Enum value in Enum.GetValues(e.GetType()))
            {
                var l = Convert.ToInt64(value);
                if (IsPowerOfTwo(l))
                {
                    if ((eLong & l) != 0)
                    {
                        yield return new SingleFlagEnum(value);
                    }
                }
            }
        }

        public static bool IsPowerOfTwo(long x)
        {
            return (x != 0) && ((x & (x - 1)) == 0);
        }
    }
}
