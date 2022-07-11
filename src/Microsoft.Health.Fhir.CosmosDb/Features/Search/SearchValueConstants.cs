// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.CosmosDb.Features.Search
{
    public static class SearchValueConstants
    {
        public const string RootAliasName = "r";

        public const string RootResourceTypeName = "rrt";

        public const string SearchIndexAliasName = "si";

        public const string ParamName = "p";

        public const string DateTimeStartName = "st";

        public const string DateTimeEndName = "et";

        public const string NumberName = "n";

        public const string LowNumberName = "ln";

        public const string HighNumberName = "hn";

        public const string NormalizedPrefix = "n_";

        public const string NormalizedStringName = NormalizedPrefix + StringName;

        public const string NormalizedTextName = NormalizedPrefix + TextName;

        public const string QuantityName = "q";

        public const string LowQuantityName = "lq";

        public const string HighQuantityName = "hq";

        public const string SystemName = "sy";

        public const string CodeName = "c";

        public const string ReferenceBaseUriName = "rb";

        public const string ReferenceResourceTypeName = "rt";

        public const string ReferenceResourceIdName = "ri";

        public const string StringName = "s";

        public const string TextName = "t";

        public const string UriName = "u";

        public const string LastModified = "lm";

        public const string SelectedFields = "r.id,r.isSystem,r.partitionKey,r.lastModified,r.rawResource,r.request,r.isDeleted,r.resourceId,r.resourceTypeName,r.isHistory,r.version,r._self,r._etag, r.searchParameterHash";

        public const string WildcardReferenceSearchParameterName = "_wildcardReference";

        public const string SortLowValueFieldName = "l";

        public const string SortHighValueFieldName = "h";
    }
}
