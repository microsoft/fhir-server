// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Reflection;
using Microsoft.Health.Fhir.Core;

namespace Microsoft.Health.Fhir.Stu3.Api.Modules
{
    public static class KnownAssemblies
    {
        public static Assembly Core => typeof(Clock).Assembly;

        public static Assembly CoreStu3 => typeof(Stu3ModelInfoProvider).Assembly;
    }
}
