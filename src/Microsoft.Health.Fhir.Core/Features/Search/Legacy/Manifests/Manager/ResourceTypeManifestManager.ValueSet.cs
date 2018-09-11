// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Core.Features.Search.Legacy.SearchValues
{
    public partial class ResourceTypeManifestManager : IResourceTypeManifestManager
    {
        private ResourceTypeManifest CreateValueSetManifest()
        {
            return CreateResourceTypeManifestBuilder<ValueSet>()
                .AddDateTimeSearchParam(SearchParamNames.Date, v => v.DateElement)
                .AddStringSearchParam(SearchParamNames.Description, v => v.Description?.Value)
                .AddUriSearchParam(SearchParamNames.Expansion, v => v.Expansion?.Identifier)
                .AddTokenSearchParam(SearchParamNames.Identifier, v => v.Identifier)
                .AddTokenSearchParam(SearchParamNames.Jurisdiction, v => v.Jurisdiction)
                .AddStringSearchParam(SearchParamNames.Name, v => v.Name)
                .AddStringSearchParam(SearchParamNames.Publisher, v => v.Publisher)
                .AddUriSearchParam(SearchParamNames.Reference, v => v.Compose?.Include, i => i.System)
                .AddTokenSearchParam(SearchParamNames.Status, v => v.Status)
                .AddStringSearchParam(SearchParamNames.Title, v => v.Title)
                .AddUriSearchParam(SearchParamNames.Url, v => v.Url)
                .AddTokenSearchParam(SearchParamNames.Version, v => v.Version)
                .ToManifest();
        }
    }
}
