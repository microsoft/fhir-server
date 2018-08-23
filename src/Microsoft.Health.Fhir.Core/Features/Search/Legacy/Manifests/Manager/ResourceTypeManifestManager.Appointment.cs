// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Core.Features.Search.Legacy.SearchValues
{
    public partial class ResourceTypeManifestManager : IResourceTypeManifestManager
    {
        private ResourceTypeManifest CreateAppointmentManifest()
        {
            return CreateResourceTypeManifestBuilder<Appointment>()
                .AddReferenceSearchParam(SearchParamNames.Actor, a => a.Participant, p => p.Actor)
                .AddTokenSearchParam(SearchParamNames.AppointmentType, a => a.AppointmentType)
                .AddDateTimeSearchParam(SearchParamNames.Date, a => a.StartElement)
                .AddTokenSearchParam(SearchParamNames.Identifier, a => a.Identifier)
                .AddReferenceSearchParam(SearchParamNames.IncomingReferral, a => a.IncomingReferral)
                .AddReferenceSearchParam(SearchParamNames.Location, a => a.Participant, p => p.Actor, FHIRAllTypes.Location)
                .AddTokenSearchParam(SearchParamNames.PartStatus, a => a.Participant, c => c.Status)
                .AddReferenceSearchParam(SearchParamNames.Patient, a => a.Participant, p => p.Actor, FHIRAllTypes.Patient)
                .AddReferenceSearchParam(SearchParamNames.Practitioner, a => a.Participant, p => p.Actor, FHIRAllTypes.Practitioner)
                .AddTokenSearchParam(SearchParamNames.ServiceType, a => a.ServiceType)
                .AddTokenSearchParam(SearchParamNames.Status, a => a.Status)
                .ToManifest();
        }
    }
}
