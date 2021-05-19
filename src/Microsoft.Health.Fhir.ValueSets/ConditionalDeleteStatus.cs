// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;

namespace Microsoft.Health.Fhir.ValueSets
{
    public static class ConditionalDeleteStatus
    {
        public const string NotSupported = "not-supported";

        [SuppressMessage("Design", "CA1720", Justification = "Name from FHIR Specification.")]
        public const string Single = "single";

        public const string Multiple = "multiple";
    }
}
