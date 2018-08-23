// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Core.Features.Search.Legacy.SearchValues
{
    public partial class ResourceTypeManifestManager : IResourceTypeManifestManager
    {
        private ResourceTypeManifest CreatePatientManifest()
        {
            return CreateResourceTypeManifestBuilder<Patient>()
                .AddTokenSearchParam(SearchParamNames.Active, p => p.Active)
                .AddAddressSearchParam(p => p.Address)
                .AddStringSearchParam(SearchParamNames.AddressCity, p => p.Address, a => a.City)
                .AddStringSearchParam(SearchParamNames.AddressCountry, p => p.Address, a => a.Country)
                .AddStringSearchParam(SearchParamNames.AddressPostalCode, p => p.Address, a => a.PostalCode)
                .AddStringSearchParam(SearchParamNames.AddressState, p => p.Address, a => a.State)
                .AddTokenSearchParam(SearchParamNames.AddressUse, p => p.Address, a => a.Use)
                .AddTokenSearchParam(SearchParamNames.AnimalBreed, p => p.Animal?.Breed)
                .AddTokenSearchParam(SearchParamNames.AnimalSpecies, p => p.Animal?.Species)
                .AddDateTimeSearchParam(SearchParamNames.Birthdate, p => p.BirthDateElement)
                .AddDateTimeSearchParam(SearchParamNames.DeathDate, p => p.Deceased as FhirDateTime)
                .AddTokenSearchParam(
                    SearchParamNames.Deceased,
                    p =>
                    {
                        switch (p.Deceased)
                        {
                            case FhirDateTime d:
                                return !string.IsNullOrEmpty(d.Value);

                            case FhirBoolean b:
                                return b.Value;

                            default:
                                return null;
                        }
                    })
                .AddTokenSearchParam(SearchParamNames.Email, p => p.Telecom, ContactPoint.ContactPointSystem.Email)
                .AddStringSearchParam(SearchParamNames.Family, p => p.Name, n => n.Family)
                .AddTokenSearchParam(SearchParamNames.Gender, p => p.Gender)
                .AddReferenceSearchParam(SearchParamNames.GeneralPractitioner, p => p.GeneralPractitioner)
                .AddStringSearchParam(SearchParamNames.Given, p => p.Name, n => n.Given)
                .AddTokenSearchParam(SearchParamNames.Identifier, p => p.Identifier)
                .AddTokenSearchParam(SearchParamNames.Language, p => p.Communication, c => c.Language)
                .AddReferenceSearchParam(SearchParamNames.Link, p => p.Link, l => l.Other)
                .AddNameSearchParam(p => p.Name)
                .AddReferenceSearchParam(SearchParamNames.Organization, p => p.ManagingOrganization)
                .AddTokenSearchParam(SearchParamNames.Phone, p => p.Telecom, ContactPoint.ContactPointSystem.Phone)
                ////.AddStringSearchParam(SearchParamConstants.Phonetic, p => p.Name) // TODO: Need to define what the phonetic matching algorithm should be.
                .AddTokenSearchParam(SearchParamNames.Telecom, p => p.Telecom)
                .ToManifest();
        }
    }
}
