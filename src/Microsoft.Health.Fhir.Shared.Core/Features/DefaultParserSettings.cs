// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Serialization;

namespace Microsoft.Health.Fhir.Core.Features
{
    public static class DefaultParserSettings
    {
        // Permissive parsing is now false by default and is no longer a setting. I'll figure out what the reprecusions of this are later.
        // The obsolete message said to use WithMode(DeserializationMode.Recoverable), but didn't say where to use that.
        public static readonly DeserializerSettings Settings = new DeserializerSettings();
    }
}
