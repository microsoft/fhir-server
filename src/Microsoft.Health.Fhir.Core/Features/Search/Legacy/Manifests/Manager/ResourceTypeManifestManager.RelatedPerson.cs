// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Core.Features.Search.Legacy.SearchValues
{
    public partial class ResourceTypeManifestManager : IResourceTypeManifestManager
    {
        private ResourceTypeManifest CreateRelatedPersonManifest()
        {
            return CreateResourceTypeManifestBuilder<RelatedPerson>()
                 .AddTokenSearchParam(SearchParamNames.Active, rp => rp.Active)
                 .AddAddressSearchParam(p => p.Address)
                 .AddStringSearchParam(SearchParamNames.AddressCity, rp => rp.Address, a => a.City)
                 .AddStringSearchParam(SearchParamNames.AddressCountry, rp => rp.Address, a => a.Country)
                 .AddStringSearchParam(SearchParamNames.AddressPostalCode, rp => rp.Address, a => a.PostalCode)
                 .AddStringSearchParam(SearchParamNames.AddressState, rp => rp.Address, a => a.State)
                 .AddTokenSearchParam(SearchParamNames.AddressUse, rp => rp.Address, a => a.Use)
                 .AddDateTimeSearchParam(SearchParamNames.Birthdate, rp => rp.BirthDateElement)
                 .AddTokenSearchParam(SearchParamNames.Email, rp => rp.Telecom, ContactPoint.ContactPointSystem.Email)
                 .AddTokenSearchParam(SearchParamNames.Gender, rp => rp.Gender)
                 .AddTokenSearchParam(SearchParamNames.Identifier, rp => rp.Identifier)
                 .AddNameSearchParam(p => p.Name)
                 .AddReferenceSearchParam(SearchParamNames.Patient, rp => rp.Patient)
                 .AddTokenSearchParam(SearchParamNames.Phone, rp => rp.Telecom, ContactPoint.ContactPointSystem.Phone)
                 ////.AddStringSearchParam(SearchParamConstants.Phonetic, rp => rp.Name) // TODO: Need to define what the phonetic matching algorithm should be.
                 .AddTokenSearchParam(SearchParamNames.Telecom, rp => rp.Telecom)
                 .ToManifest();
        }
    }
}
