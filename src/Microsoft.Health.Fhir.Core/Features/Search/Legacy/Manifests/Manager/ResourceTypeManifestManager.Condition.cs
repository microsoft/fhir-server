// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Core.Features.Search.Legacy.SearchValues
{
    public partial class ResourceTypeManifestManager : IResourceTypeManifestManager
    {
        private ResourceTypeManifest CreateConditionManifest()
        {
            return CreateResourceTypeManifestBuilder<Condition>()
                .AddQuantitySearchParam(SearchParamNames.AbatementAge, c => c.Abatement as Age)
                ////.AddQuantitySearchParam(SearchParamNames.AbatementAge, c => c.Abatement as Range)
                .AddTokenSearchParam(
                    SearchParamNames.AbatementBoolean,
                    c =>
                    {
                        switch (c.Abatement)
                        {
                            case FhirBoolean b:
                                return b.Value;
                            case FhirDateTime d:
                            case Age a:
                            case Period p:
                            case Range r:
                            case FhirString s:
                                return true;
                            default:
                                return null;
                        }
                    })
                .AddDateTimeSearchParam(SearchParamNames.AbatementDate, c => c.Abatement as FhirDateTime)
                .AddDateTimeSearchParam(SearchParamNames.AbatementDate, c => c.Abatement as Period)
                .AddStringSearchParam(SearchParamNames.AbatementString, c => c.Abatement as FhirString)
                .AddDateTimeSearchParam(SearchParamNames.AssertedDate, c => c.AssertedDateElement)
                .AddReferenceSearchParam(SearchParamNames.Asserter, c => c.Asserter)
                .AddTokenSearchParam(SearchParamNames.BodySite, c => c.BodySite)
                .AddTokenSearchParam(SearchParamNames.Category, c => c.Category)
                .AddTokenSearchParam(SearchParamNames.ClinicalStatus, c => c.ClinicalStatus)
                .AddTokenSearchParam(SearchParamNames.Code, c => c.Code)
                .AddReferenceSearchParam(SearchParamNames.Context, c => c.Context)
                .AddReferenceSearchParam(SearchParamNames.Encounter, c => c.Context, FHIRAllTypes.Encounter)
                .AddTokenSearchParam(SearchParamNames.Evidence, c => c.Evidence, e => e.Code)
                .AddReferenceSearchParam(SearchParamNames.EvidenceDetail, c => c.Evidence, e => e.Detail)
                .AddTokenSearchParam(SearchParamNames.Identifier, c => c.Identifier)
                .AddQuantitySearchParam(SearchParamNames.OnsetAge, c => c.Onset as Age)
                ////.AddQuantitySearchParam(SearchParamNames.OnsetAge, c => c.Onset as Range)
                .AddDateTimeSearchParam(SearchParamNames.OnsetDate, c => c.Onset as FhirDateTime)
                .AddDateTimeSearchParam(SearchParamNames.OnsetDate, c => c.Onset as Period)
                .AddStringSearchParam(SearchParamNames.OnsetInfo, c => c.Onset as FhirString)
                .AddReferenceSearchParam(SearchParamNames.Patient, c => c.Subject, FHIRAllTypes.Patient)
                .AddTokenSearchParam(SearchParamNames.Severity, c => c.Severity)
                .AddTokenSearchParam(SearchParamNames.Stage, c => c.Stage?.Summary)
                .AddReferenceSearchParam(SearchParamNames.Subject, c => c.Subject)
                .AddTokenSearchParam(SearchParamNames.VerificationStatus, c => c.VerificationStatus)
                .ToManifest();
        }
    }
}
