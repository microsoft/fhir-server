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
        public const string At = "_at";

        public const string Before = "_before";

        /// <summary>
        /// The continuation token parameter.
        /// </summary>
        public const string ContinuationToken = "ct";

        public const string Count = "_count";

        /// <summary>
        /// The format query parameter.
        /// </summary>
        public const string Format = "_format";

        public const string LastUpdated = "_lastUpdated";

        public const string Mode = "mode";

        /// <summary>
        /// The pretty query parameter.
        /// </summary>
        /// <remarks>True if the client wishes to request for pretty-printed resources (either in JSON or XML), false otherwise.</remarks>
        public const string Pretty = "_pretty";

        public const string Profile = "profile";

        public const string Since = "_since";

        public const string Sort = "_sort";

        public const string Summary = "_summary";

        public const string Elements = "_elements";

        /// <summary>
        /// The total query parameter.
        /// </summary>
        /// <remarks>Specifies if the total number of matching search results should be included in the returned Bundle.</remarks>
        public const string Total = "_total";

        public const string List = "_list";

        public const string Id = "_id";

        public const string Type = "_type";

        public const string Container = "_container";

        /// <summary>
        /// The anonymization configuration
        /// </summary>
        /// <remarks>The anonymization configuration location and addition information. </remarks>
        public const string AnonymizationConfigurationLocation = "_anonymizationConfig";

        public const string AnonymizationConfigurationCollectionReference = "_anonymizationConfigCollectionReference";

        public const string AnonymizationConfigurationFileEtag = "_anonymizationConfigEtag";

        public const string OutputFormat = "_outputFormat";

        public const string TypeFilter = "_typeFilter";

        public const string Text = "_text";

        public const string Till = "_till";

        public const string IsParallel = "_isParallel";

        public const string StartSurrogateId = "_startSurrogateId";

        public const string EndSurrogateId = "_endSurrogateId";

        public const string GlobalStartSurrogateId = "_globalStartSurrogateId";

        public const string GlobalEndSurrogateId = "_globalEndSurrogateId";

        public const string IgnoreSearchParamHash = "_ignoreSearchParamHash";

        /// <summary>
        /// Frequently used SearchParameters
        /// </summary>
        public const string Identifier = "identifier";
    }
}
