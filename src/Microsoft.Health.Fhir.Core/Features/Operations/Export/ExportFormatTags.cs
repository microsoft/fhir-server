// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Configs;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export
{
    /// <summary>
    /// Class for the supported tags in export format definitions. See <see cref="ExportJobFormatConfiguration"/>
    /// </summary>
    public static class ExportFormatTags
    {
        public const string ResourceName = "<resourcename>";

        public const string Timestamp = "<timestamp>";

        public const string Id = "<id>";
    }
}
