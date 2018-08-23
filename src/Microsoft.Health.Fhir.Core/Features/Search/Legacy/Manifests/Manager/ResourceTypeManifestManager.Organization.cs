// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Core.Features.Search.Legacy.SearchValues
{
    public partial class ResourceTypeManifestManager : IResourceTypeManifestManager
    {
        private ResourceTypeManifest CreateOrganizationManifest()
        {
            return CreateResourceTypeManifestBuilder<Organization>()
                .AddTokenSearchParam(SearchParamNames.Active, o => o.Active)
                .AddStringSearchParam(SearchParamNames.AddressCity, o => o.Address, a => a.City)
                .AddStringSearchParam(SearchParamNames.AddressCountry, o => o.Address, a => a.Country)
                .AddStringSearchParam(SearchParamNames.AddressPostalCode, o => o.Address, a => a.PostalCode)
                .AddStringSearchParam(SearchParamNames.AddressState, o => o.Address, a => a.State)
                .AddTokenSearchParam(SearchParamNames.AddressUse, o => o.Address, a => a.Use)
                .AddReferenceSearchParam(SearchParamNames.Endpoint, o => o.Endpoint)
                .AddTokenSearchParam(SearchParamNames.Identifier, o => o.Identifier)
                .AddStringSearchParam(SearchParamNames.Name, o => o.Name)
                .AddReferenceSearchParam(SearchParamNames.PartOf, o => o.PartOf)
                ////.AddStringSearchParam(SearchParamNames.Phonetic, o => o.Name) // TODO: Need to define what the phonetic matching algorithm should be.
                .AddTokenSearchParam(SearchParamNames.Type, o => o.Type)
                .ToManifest();
        }
    }
}
