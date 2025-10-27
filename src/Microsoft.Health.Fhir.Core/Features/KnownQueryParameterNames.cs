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
        /// This setting is currently set by:
        ///     x-ms-query-latency-over-efficiency
        ///     x-conditionalquery-processing-logic
        /// It is used to hint that the request should run with a max parallel setting.
        /// </summary>
        public const string OptimizeConcurrency = "_optimizeConcurrency";

        /// <summary>
        /// This setting is controlled by the x-ms-query-cache-enabled header. It controls whether to use the query cache or not.
        /// </summary>
        public const string QueryCaching = "_queryCaching";

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

        public const string FeedRange = "_feedRange";

        /// <summary>
        /// Frequently used SearchParameters
        /// </summary>
        public const string Identifier = "identifier";

        public const string BulkHardDelete = "_hardDelete";

        public const string HardDelete = "hardDelete";

        public const string PurgeHistory = "_purgeHistory";

        /// <summary>
        /// Used by $export as a comma-separated list of parameters instructing which initial data should be included.
        /// </summary>
        public const string IncludeAssociatedData = "includeAssociatedData";

        /// <summary>
        /// Used by export and bulk-update to specify the number of resources to be processed by the search engine.
        /// </summary>
        public const string MaxCount = "_maxCount";

        public const string NotReferenced = "_not-referenced";

        public const string RemoveReferences = "_remove-references";

        /// <summary>
        /// The $includes continuation token parameter.
        /// </summary>
        public const string IncludesContinuationToken = "includesCt";

        /// <summary>
        /// The $includes count parameter.
        /// </summary>
        public const string IncludesCount = "_includesCount";

        /// <summary>
        /// The excluded resource types parameter representing a comma separated list of resource types.
        /// </summary>
        public const string ExcludedResourceTypes = "excludedResourceTypes";

        public const string ReverseInclude = "_revinclude";

        public const string ReturnDetails = "_details";

        public const string MetaHistory = "_meta-history";
    }
}
