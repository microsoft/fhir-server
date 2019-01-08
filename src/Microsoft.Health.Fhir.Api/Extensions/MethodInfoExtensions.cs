// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EnsureThat;

namespace Microsoft.Health.Fhir.Api.Extensions
{
    public static class MethodInfoExtensions
    {
        public static IEnumerable<T> GetCustomAttributes<T>(this MethodInfo methodInfo, bool inherit = false)
            where T : Attribute
        {
            EnsureArg.IsNotNull(methodInfo, nameof(methodInfo));

            return methodInfo.GetCustomAttributes(typeof(T), inherit)?.Cast<T>();
        }
    }
}
