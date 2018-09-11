// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Core.Features.Search.Legacy.SearchValues
{
    public partial class ResourceTypeManifestManager : IResourceTypeManifestManager
    {
        private ResourceTypeManifest CreateQuestionnaireManifest()
        {
            return CreateResourceTypeManifestBuilder<Questionnaire>()
                .AddTokenSearchParam(SearchParamNames.Code, q => q.Item, i => i.Code)
                .AddDateTimeSearchParam(SearchParamNames.Date, q => q.Date)
                .AddStringSearchParam(SearchParamNames.Description, q => q.Description?.Value)
                .AddDateTimeSearchParam(SearchParamNames.Effective, q => q.EffectivePeriod as Period)
                .AddTokenSearchParam(SearchParamNames.Identifier, q => q.Identifier)
                .AddTokenSearchParam(SearchParamNames.Jurisdiction, q => q.Jurisdiction)
                .AddStringSearchParam(SearchParamNames.Name, q => q.Name)
                .AddStringSearchParam(SearchParamNames.Publisher, q => q.Publisher)
                .AddTokenSearchParam(SearchParamNames.Status, q => q.Status)
                .AddStringSearchParam(SearchParamNames.Title, q => q.Title)
                .AddUriSearchParam(SearchParamNames.Url, q => q.Url)
                .AddTokenSearchParam(SearchParamNames.Version, q => q.Version)
                .ToManifest();
        }
    }
}
