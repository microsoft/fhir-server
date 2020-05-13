// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Globalization;

namespace Microsoft.Health.Fhir.Core.Properties
{
    [AttributeUsage(AttributeTargets.Assembly)]
    internal class AssemblyBuildDateAttribute : Attribute
    {
        public AssemblyBuildDateAttribute(string value)
        {
            DateTime = DateTimeOffset.ParseExact(value, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None);
        }

        public DateTimeOffset DateTime { get; }
    }
}
