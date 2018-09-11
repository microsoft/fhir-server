// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Core.Features.Search.Legacy.SearchValues
{
    public partial class ResourceTypeManifestManager : IResourceTypeManifestManager
    {
        private ResourceTypeManifest CreateObservationManifest()
        {
            return CreateResourceTypeManifestBuilder<Observation>()
                .AddReferenceSearchParam(SearchParamNames.BasedOn, o => o.BasedOn)
                .AddTokenSearchParam(SearchParamNames.Category, o => o.Category)
                .AddTokenSearchParam(SearchParamNames.Code, o => o.Code)
                .AddCompositeTokenSearchParam(SearchParamNames.CodeValueConcept, o => o.Code, o => o.Value as CodeableConcept)
                .AddCompositeDateTimeSearchParam(SearchParamNames.CodeValueDate, o => o.Code, o => o.Value as FhirDateTime)
                .AddCompositeQuantitySearchParam(SearchParamNames.CodeValueQuantity, o => o.Code, o => o.Value as Quantity)
                .AddCompositeStringSearchParam(SearchParamNames.CodeValueString, o => o.Code, o => o.Value as FhirString)
                .AddTokenSearchParam(SearchParamNames.ComboCode, o => o.Code)
                .AddTokenSearchParam(SearchParamNames.ComboCode, o => o.Component, c => c.Code)
                .AddCompositeTokenSearchParam(SearchParamNames.ComboCodeValueConcept, o => o.Code, o => o.Value as CodeableConcept)
                .AddCompositeTokenSearchParam(SearchParamNames.ComboCodeValueConcept, o => o.Component, c => c.Code, c => c.Value as CodeableConcept)
                .AddCompositeQuantitySearchParam(SearchParamNames.ComboCodeValueQuantity, o => o.Code, o => o.Value as Quantity)
                .AddCompositeQuantitySearchParam(SearchParamNames.ComboCodeValueQuantity, o => o.Component, q => q.Code, q => q.Value as Quantity)
                .AddTokenSearchParam(SearchParamNames.ComboDataAbsentReason, o => o.DataAbsentReason)
                .AddTokenSearchParam(SearchParamNames.ComboDataAbsentReason, o => o.Component, c => c.DataAbsentReason)
                .AddTokenSearchParam(SearchParamNames.ComboValueConcept, o => o.Value as CodeableConcept)
                .AddTokenSearchParam(SearchParamNames.ComboValueConcept, o => o.Component, c => c.Value as CodeableConcept)
                .AddQuantitySearchParam(SearchParamNames.ComboValueQuantity, o => o.Value as Quantity)
                .AddQuantitySearchParam(SearchParamNames.ComboValueQuantity, o => o.Component, c => c.Value as Quantity)
                .AddTokenSearchParam(SearchParamNames.ComponentCode, o => o.Component, c => c.Code)
                .AddCompositeTokenSearchParam(SearchParamNames.ComponentCodeValueConcept, o => o.Component, c => c.Code, c => c.Value as CodeableConcept)
                .AddCompositeQuantitySearchParam(SearchParamNames.ComponentCodeValueQuantity, o => o.Component, c => c.Code, c => c.Value as Quantity)
                .AddTokenSearchParam(SearchParamNames.ComponentDataAbsentReason, o => o.Component, c => c.DataAbsentReason)
                .AddTokenSearchParam(SearchParamNames.ComponentValueConcept, o => o.Component, c => c.Value as CodeableConcept)
                .AddQuantitySearchParam(SearchParamNames.ComponentValueQuantity, o => o.Component, c => c.Value as Quantity)
                .AddReferenceSearchParam(SearchParamNames.Context, o => o.Context)
                .AddTokenSearchParam(SearchParamNames.DataAbsentReason, o => o.DataAbsentReason)
                .AddDateTimeSearchParam(SearchParamNames.Date, o => o.Effective as FhirDateTime)
                .AddDateTimeSearchParam(SearchParamNames.Date, o => o.Effective as Period)
                .AddReferenceSearchParam(SearchParamNames.Device, o => o.Device)
                .AddReferenceSearchParam(SearchParamNames.Encounter, o => o.Context, FHIRAllTypes.Encounter)
                .AddTokenSearchParam(SearchParamNames.Identifier, o => o.Identifier)
                .AddTokenSearchParam(SearchParamNames.Method, o => o.Method)
                .AddReferenceSearchParam(SearchParamNames.Patient, o => o.Subject, FHIRAllTypes.Patient)
                .AddReferenceSearchParam(SearchParamNames.Performer, o => o.Performer)
                .AddCompositeReferenceSearchParam(SearchParamNames.Related, o => o.Related, r => r.Type, r => r.Target)
                .AddReferenceSearchParam(SearchParamNames.RelatedTarget, o => o.Related, r => r.Target)
                .AddTokenSearchParam(SearchParamNames.RelatedType, o => o.Related, r => r.Type)
                .AddReferenceSearchParam(SearchParamNames.Specimen, o => o.Specimen)
                .AddTokenSearchParam(SearchParamNames.Status, o => o.Status)
                .AddReferenceSearchParam(SearchParamNames.Subject, o => o.Subject)
                .AddTokenSearchParam(SearchParamNames.ValueConcept, o => o.Value as CodeableConcept)
                .AddDateTimeSearchParam(SearchParamNames.ValueDate, o => o.Value as FhirDateTime)
                .AddDateTimeSearchParam(SearchParamNames.ValueDate, o => o.Value as Period)
                .AddQuantitySearchParam(SearchParamNames.ValueQuantity, o => o.Value as Quantity)
                .AddStringSearchParam(SearchParamNames.ValueString, o => o.Value as FhirString)
                .ToManifest();
        }
    }
}
