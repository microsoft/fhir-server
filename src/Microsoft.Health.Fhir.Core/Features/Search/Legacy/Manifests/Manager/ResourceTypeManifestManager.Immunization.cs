// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Core.Features.Search.Legacy.SearchValues
{
    public partial class ResourceTypeManifestManager : IResourceTypeManifestManager
    {
        private ResourceTypeManifest CreateImmunizationManifest()
        {
            return new ResourceTypeManifestBuilder<Immunization>(_searchParamFactory)
                .AddDateTimeSearchParam(SearchParamNames.Date, i => i.DateElement)
                .AddNumberSearchParam(SearchParamNames.DoseSequence, i => i.VaccinationProtocol, v => v.DoseSequence)
                .AddTokenSearchParam(SearchParamNames.Identifier, i => i.Identifier)
                .AddReferenceSearchParam(SearchParamNames.Location, i => i.Location)
                .AddStringSearchParam(SearchParamNames.LotNumber, i => i.LotNumber)
                .AddReferenceSearchParam(SearchParamNames.Manufacturer, i => i.Manufacturer)
                .AddTokenSearchParam(SearchParamNames.NotGiven, i => i.NotGiven)
                .AddReferenceSearchParam(SearchParamNames.Patient, i => i.Patient)
                .AddReferenceSearchParam(SearchParamNames.Practitioner, i => i.Practitioner, p => p.Actor)
                .AddReferenceSearchParam(SearchParamNames.Reaction, i => i.Reaction, r => r.Detail)
                .AddDateTimeSearchParam(SearchParamNames.ReactionDate, i => i.Reaction, r => r.DateElement)
                .AddTokenSearchParam(SearchParamNames.Reason, i => i.Explanation?.Reason)
                .AddTokenSearchParam(SearchParamNames.ReasonNotGiven, i => i.Explanation?.ReasonNotGiven)
                .AddTokenSearchParam(SearchParamNames.Status, i => i.Status)
                .AddTokenSearchParam(SearchParamNames.VaccineCode, i => i.VaccineCode)
                .ToManifest();
        }
    }
}
