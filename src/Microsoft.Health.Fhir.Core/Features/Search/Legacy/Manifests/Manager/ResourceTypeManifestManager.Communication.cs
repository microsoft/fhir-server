// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Core.Features.Search.Legacy.SearchValues
{
    public partial class ResourceTypeManifestManager : IResourceTypeManifestManager
    {
        private ResourceTypeManifest CreateCommunicationManifest()
        {
            return CreateResourceTypeManifestBuilder<Communication>()
                .AddReferenceSearchParam(SearchParamNames.BasedOn, c => c.BasedOn)
                .AddTokenSearchParam(SearchParamNames.Category, c => c.Category)
                .AddReferenceSearchParam(SearchParamNames.Context, c => c.Context)
                .AddReferenceSearchParam(SearchParamNames.Definition, c => c.Definition)
                .AddReferenceSearchParam(SearchParamNames.Encounter, c => c.Context, FHIRAllTypes.Encounter)
                .AddTokenSearchParam(SearchParamNames.Identifier, c => c.Identifier)
                .AddTokenSearchParam(SearchParamNames.Medium, c => c.Medium)
                .AddReferenceSearchParam(SearchParamNames.PartOfWithDash, c => c.PartOf)
                .AddReferenceSearchParam(SearchParamNames.Patient, c => c.Subject)
                .AddDateTimeSearchParam(SearchParamNames.Received, c => c.ReceivedElement)
                .AddReferenceSearchParam(SearchParamNames.Recipient, c => c.Recipient)
                .AddReferenceSearchParam(SearchParamNames.Sender, c => c.Sender)
                .AddDateTimeSearchParam(SearchParamNames.Sent, c => c.SentElement)
                .AddTokenSearchParam(SearchParamNames.Status, c => c.Status)
                .AddReferenceSearchParam(SearchParamNames.Subject, c => c.Subject)
                .ToManifest();
        }
    }
}
