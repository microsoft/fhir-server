// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;

namespace Microsoft.Health.Fhir.Core.Features.Conformance
{
    public static class TerminologyOperationParameterNames
    {
        internal static class Expand
        {
            // https://hl7.org/fhir/R4/valueset-operation-expand.html
            public const string Url = "url";
            public const string ValueSet = "valueSet";
            public const string ValueSetVersion = "valueSetVersion";
            public const string Context = "context";
            public const string ContextDirection = "contextDirection";
            public const string Filter = "filter";
            public const string Date = "date";
            public const string Offset = "offset";
            public const string Count = "count";
            public const string IncludeDesignations = "includeDesignations";
            public const string Designation = "designation";
            public const string IncludeDefinition = "includeDefinition";
            public const string ActiveOnly = "activeOnly";
            public const string ExcludeNested = "excludeNested";
            public const string ExcludeNotForUI = "excludeNotForUI";
            public const string ExcludePostCoordinated = "excludePostCoordinated";
            public const string DisplayLanguage = "displayLanguage";
            public const string ExcludeSystem = "exclude-system";
            public const string SystemVersion = "system-version";
            public const string CheckSystemVersion = "check-system-version";
            public const string ForceSystemVersion = "force-system-version";

            public static readonly IReadOnlyList<string> Names = new List<string>
            {
                Url,
                ValueSet,
                ValueSetVersion,
                Context,
                ContextDirection,
                Filter,
                Date,
                Offset,
                Count,
                IncludeDesignations,
                Designation,
                IncludeDefinition,
                ActiveOnly,
                ExcludeNested,
                ExcludeNotForUI,
                ExcludePostCoordinated,
                DisplayLanguage,
                ExcludeSystem,
                SystemVersion,
                CheckSystemVersion,
                ForceSystemVersion,
            };
        }
    }
}
