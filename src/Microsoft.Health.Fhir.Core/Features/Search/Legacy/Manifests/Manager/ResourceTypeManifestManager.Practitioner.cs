// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Core.Features.Search.Legacy.SearchValues
{
    public partial class ResourceTypeManifestManager : IResourceTypeManifestManager
    {
        private ResourceTypeManifest CreatePractitionerManifest()
        {
            return CreateResourceTypeManifestBuilder<Practitioner>()
                .AddTokenSearchParam(SearchParamNames.Active, p => p.Active)
                .AddAddressSearchParam(p => p.Address)
                .AddStringSearchParam(SearchParamNames.AddressCity, p => p.Address, a => a.City)
                .AddStringSearchParam(SearchParamNames.AddressCountry, p => p.Address, a => a.Country)
                .AddStringSearchParam(SearchParamNames.AddressPostalCode, p => p.Address, a => a.PostalCode)
                .AddStringSearchParam(SearchParamNames.AddressState, p => p.Address, a => a.State)
                .AddTokenSearchParam(SearchParamNames.AddressUse, p => p.Address, a => a.Use)
                .AddTokenSearchParam(SearchParamNames.Communication, p => p.Communication)
                .AddTokenSearchParam(SearchParamNames.Email, p => p.Telecom, ContactPoint.ContactPointSystem.Email)
                .AddStringSearchParam(SearchParamNames.Family, p => p.Name, n => n.Family)
                .AddTokenSearchParam(SearchParamNames.Gender, p => p.Gender)
                .AddStringSearchParam(SearchParamNames.Given, p => p.Name, n => n.Given)
                .AddTokenSearchParam(SearchParamNames.Identifier, p => p.Identifier)
                .AddNameSearchParam(p => p.Name)
                .AddTokenSearchParam(SearchParamNames.Phone, p => p.Telecom, ContactPoint.ContactPointSystem.Phone)
                ////.AddStringSearchParam(SearchParamConstants.Phonetic, p => p.Name) // TODO: Need to define what the phonetic matching algorithm should be.
                .AddTokenSearchParam(SearchParamNames.Telecom, p => p.Telecom)
                .ToManifest();
        }
    }
}
