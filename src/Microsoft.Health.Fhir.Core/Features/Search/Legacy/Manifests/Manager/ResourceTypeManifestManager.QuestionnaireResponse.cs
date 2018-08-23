// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Core.Features.Search.Legacy.SearchValues
{
    public partial class ResourceTypeManifestManager : IResourceTypeManifestManager
    {
        private ResourceTypeManifest CreateQuestionnaireResponseManifest()
        {
            return CreateResourceTypeManifestBuilder<QuestionnaireResponse>()
                .AddReferenceSearchParam(SearchParamNames.Author, q => q.Author)
                .AddDateTimeSearchParam(SearchParamNames.Authored, q => q.Authored)
                .AddReferenceSearchParam(SearchParamNames.BasedOn, q => q.BasedOn)
                .AddReferenceSearchParam(SearchParamNames.Context, q => q.Context)
                .AddTokenSearchParam(SearchParamNames.Identifier, q => q.Identifier)
                .AddReferenceSearchParam(SearchParamNames.Parent, q => q.Parent)
                .AddReferenceSearchParam(SearchParamNames.Patient, q => q.Subject, FHIRAllTypes.Patient)
                .AddReferenceSearchParam(SearchParamNames.Questionnaire, q => q.Questionnaire)
                .AddReferenceSearchParam(SearchParamNames.Source, q => q.Source)
                .AddTokenSearchParam(SearchParamNames.Status, q => q.Status)
                .AddReferenceSearchParam(SearchParamNames.Subject, q => q.Subject)
                .ToManifest();
        }
    }
}
