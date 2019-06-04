// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Hl7.Fhir.ElementModel;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.Common
{
    public interface IVersionSpecificFhirClient
    {
        Format Format { get; }

        (bool SecurityEnabled, string AuthorizeUrl, string TokenUrl) SecuritySettings { get; }

        HttpClient HttpClient { get; }

        Task RunAsUser(TestUser user, TestApplication clientApplication);

        Task RunAsClientApplication(TestApplication clientApplication);

        Task<FhirResponse<ResourceElement>> CreateAsync(ResourceElement resource);

        Task<FhirResponse<ResourceElement>> CreateAsync(string uri, ResourceElement resource);

        Task<FhirResponse<ResourceElement>> ReadAsync(string resourceType, string resourceId);

        Task<FhirResponse<ResourceElement>> ReadAsync(string uri);

        Task<FhirResponse<ResourceElement>> VReadAsync(string resourceType, string resourceId, string versionId);

        Task<FhirResponse<ResourceElement>> UpdateAsync(ResourceElement resource, string ifMatchVersion = null);

        Task<FhirResponse<ResourceElement>> UpdateAsync(string uri, ResourceElement resource, string ifMatchVersion = null);

        Task<FhirResponse> DeleteAsync(ResourceElement resource);

        Task<FhirResponse> DeleteAsync(string uri);

        Task<FhirResponse> HardDeleteAsync(ResourceElement resource);

        Task<FhirResponse<ResourceElement>> SearchAsync(string resourceType, string query = null, int? count = null);

        Task<FhirResponse<ResourceElement>> SearchAsync(string url);

        Task<FhirResponse<ResourceElement>> SearchPostAsync(string resourceType, params (string key, string value)[] body);

        Task<string> ExportAsync(Dictionary<string, string> queryParams);

        Task EnsureSuccessStatusCodeAsync(HttpResponseMessage response);

        Task<FhirResponse<ResourceElement>> CreateResourceElementResponseAsync(HttpResponseMessage response);

        Task SetupAuthenticationAsync(TestApplication clientApplication, TestUser user = null);

        Task<string> GetBearerToken(TestApplication clientApplication, TestUser user);

        List<KeyValuePair<string, string>> GetAppSecuritySettings(TestApplication clientApplication);

        List<KeyValuePair<string, string>> GetUserSecuritySettings(TestApplication clientApplication, TestUser user);

        Task GetSecuritySettings(string fhirServerMetadataUrl);

        ResourceElement GetDefaultObservation();

        ResourceElement GetDefaultPatient();

        ResourceElement GetDefaultOrganization();

        ResourceElement GetEmptyRiskAssessment();

        ResourceElement GetEmptyObservation();

        ResourceElement GetEmptyValueSet();

        ResourceElement GetJsonSample(string fileName);

        void Validate(ResourceElement resourceElement);

        ResourceElement UpdateId(ResourceElement resourceElement, string id);

        ResourceElement UpdateVersion(ResourceElement resourceElement, string newVersion);

        ResourceElement UpdateLastUpdated(ResourceElement resourceElement, DateTimeOffset lastUpdated);

        ResourceElement UpdateText(ResourceElement resourceElement, string text);

        ResourceElement UpdatePatientFamilyName(ResourceElement resourceElement, string familyName);

        ResourceElement UpdatePatientAddressCity(ResourceElement resourceElement, string city);

        ResourceElement UpdatePatientGender(ResourceElement resourceElement, string gender);

        ResourceElement UpdateObservationStatus(ResourceElement resourceElement, string status);

        ResourceElement UpdateObservationValueQuantity(ResourceElement resourceElement, decimal quantity, string unit, string system);

        ResourceElement UpdateObservationValueCodeableConcept(ResourceElement resourceElement, string system, string code, string text, (string system, string code, string display)[] codings);

        ResourceElement UpdateValueSetStatus(ResourceElement resourceElement, string status);

        ResourceElement UpdateValueSetUrl(ResourceElement resourceElement, string url);

        ResourceElement AddObservationCoding(ResourceElement resourceElement, string system, string code);

        ResourceElement UpdateObservationEffectiveDate(ResourceElement resourceElement, string date);

        ResourceElement AddMetaTag(ResourceElement resourceElement, string system, string code);

        ResourceElement AddObservationIdentifier(ResourceElement resourceElement, string system, string identifier);

        ResourceElement AddDocumentReferenceIdentifier(ResourceElement resourceElement, string system, string identifier);

        ResourceElement UpdateRiskAssessmentSubject(ResourceElement resourceElement, string subject);

        ResourceElement UpdateRiskAssessmentStatus(ResourceElement resourceElement, string status);

        ResourceElement UpdateRiskAssessmentProbability(ResourceElement resourceElement, int probability);

        ResourceElement UpdatePatientManagingOrganization(ResourceElement resourceElement, string reference);

        ResourceElement UpdateObservationSubject(ResourceElement resourceElement, string reference);

        ResourceElement UpdateEncounterSubject(ResourceElement resourceElement, string reference);

        ResourceElement UpdateConditionSubject(ResourceElement resourceElement, string reference);

        ResourceElement UpdateObservationDevice(ResourceElement resourceElement, string reference);

        bool Compare(ResourceElement expected, ITypedElement actual);

        bool Compare(ResourceElement expected, ResourceElement actual);
    }
}
