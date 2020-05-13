// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Globalization;
using System.Reflection;

namespace Microsoft.Health.Fhir.Core.Properties
{
    internal static class AssemblyInformation
    {
        public static DateTimeOffset BuildDate()
        {
            AssemblyBuildDateAttribute attribute = typeof(AssemblyInformation).Assembly.GetCustomAttribute<AssemblyBuildDateAttribute>();
            return attribute?.DateTime ?? default;
        }
    }
}
