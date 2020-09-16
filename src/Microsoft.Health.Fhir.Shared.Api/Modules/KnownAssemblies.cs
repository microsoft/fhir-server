// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Health.Fhir.Core;
using Microsoft.Health.Fhir.Core.Exceptions;

namespace Microsoft.Health.Fhir.Api.Modules
{
    public static class KnownAssemblies
    {
        [SuppressMessage(category: "Design", checkId: "CA1819", Justification = "This property is used in methods that require an array")]
        public static Assembly[] All => new[] { Core, CoreVersionSpecific, ApiVersionSpecific };

        public static Assembly Core => typeof(FhirException).Assembly;

        public static Assembly CoreVersionSpecific => typeof(VersionSpecificModelInfoProvider).Assembly;

        public static Assembly ApiVersionSpecific => typeof(KnownAssemblies).Assembly;
    }
}
