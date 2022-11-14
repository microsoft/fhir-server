// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
namespace Microsoft.Health.Fhir.Api.Features.AnonymousOperations
{
    /// <summary>
    /// Value set for Fhir operations which do not require authorization
    /// </summary>
    public static class FhirAnonymousOperationType
    {
        public const string Metadata = "metadata";

        public const string Versions = "versions";

        public const string WellKnown = "wellknown";
    }
}
