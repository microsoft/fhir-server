// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Reflection;
using Microsoft.Health.Fhir.Core;
using Microsoft.Health.Fhir.Core.Exceptions;

namespace Microsoft.Health.Fhir.Api.Modules
{
    public static class KnownAssemblies
    {
        public static Assembly Core => typeof(FhirException).Assembly;

        public static Assembly CoreVersionSpecific => typeof(VersionSpecificModelInfoProvider).Assembly;
    }
}
