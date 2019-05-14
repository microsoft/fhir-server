// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Rest;

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
        public const string Format = HttpUtil.RESTPARAM_FORMAT;

        public const string At = "_at";

        /// <summary>
        /// The continuation token parameter.
        /// </summary>
        public const string ContinuationToken = "ct";

        public const string Count = HttpUtil.HISTORY_PARAM_COUNT;

        public const string Since = HttpUtil.HISTORY_PARAM_SINCE;

        public const string DestinationType = "_destinationType";

        public const string DestinationConnectionSettings = "_destinationConnectionSettings";
    }
}
