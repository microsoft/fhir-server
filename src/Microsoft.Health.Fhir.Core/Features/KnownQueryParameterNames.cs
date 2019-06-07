// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features
{
    /// <summary>
    /// Provides list of known query parameter names.
    /// </summary>
    public static class KnownQueryParameterNames
    {
        /// <summary>
        /// The format query parameter.
        /// </summary>
        public const string Format = "_format";

        public const string At = "_at";

        /// <summary>
        /// The continuation token parameter.
        /// </summary>
        public const string ContinuationToken = "ct";

        public const string Count = "_count";

        public const string Since = "_since";

        public const string LastUpdated = "_lastUpdated";

        public const string Before = "_before";

        public const string DestinationType = "_destinationType";

        public const string DestinationConnectionSettings = "_destinationConnectionSettings";
    }
}
