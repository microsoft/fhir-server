// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Serialization;

namespace Microsoft.Health.Fhir.Core.Features
{
    internal class DefaultParserSettings
    {
        public static readonly ParserSettings Settings = new ParserSettings { PermissiveParsing = true };
    }
}
