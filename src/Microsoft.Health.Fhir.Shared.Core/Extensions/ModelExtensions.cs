// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Extensions
{
    public static class ModelExtensions
    {
        /// <summary>
        /// This method provides temporary compatibility while STU3/R4 compatibility is added
        /// </summary>
        public static void SetModelInfoProvider()
        {
#if Stu3
            ModelInfoProvider.SetProvider(new Stu3ModelInfoProvider());
#elif R4
            ModelInfoProvider.SetProvider(new R4ModelInfoProvider());
#endif
        }

        public static CodeableConcept ToPoco(this CodingInfo model)
        {
            EnsureArg.IsNotNull(model, nameof(model));

            return new CodeableConcept(model.System, model.Code);
        }

        public static OperationOutcome.IssueComponent ToPoco(this OperationOutcomeIssue issue)
        {
            EnsureArg.IsNotNull(issue, nameof(issue));

            return new OperationOutcome.IssueComponent
            {
                Severity = Enum.Parse<OperationOutcome.IssueSeverity>(issue.Severity),
                Code = Enum.Parse<OperationOutcome.IssueType>(issue.Code),
                Diagnostics = issue.Diagnostics,
                Location = issue.Location,
            };
        }

        public static ResourceElement ToResourceElement(this Base resource)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            return resource.ToTypedElement().ToResourceElement();
        }

        public static T ToPoco<T>(this ResourceElement resource)
            where T : Resource
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            return (T)resource.ResourceInstance ?? resource.Instance.ToPoco<T>();
        }

        public static Resource ToPoco(this ResourceElement resource)
        {
            return ToPoco<Resource>(resource);
        }

        public static ResourceElement UpdateId(this ResourceElement resource, string newId)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            var poco = resource.ToPoco();
            poco.Id = newId;
            return poco.ToResourceElement();
        }

        public static ResourceElement UpdateVersion(this ResourceElement resource, string newVersion)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            var poco = resource.ToPoco();
            poco.VersionId = newVersion;
            return poco.ToResourceElement();
        }

        public static ResourceElement UpdateLastUpdated(this ResourceElement resource, DateTimeOffset lastUpdated)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            var poco = resource.ToPoco();

            if (poco.Meta == null)
            {
                poco.Meta = new Meta();
            }

            poco.Meta.LastUpdated = lastUpdated;
            return poco.ToResourceElement();
        }

        public static ResourceElement UpdateText(this ResourceElement resource, string text)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            var poco = resource.ToPoco<DomainResource>();

            if (poco.Text == null)
            {
                poco.Text = new Narrative();
            }

            poco.Text.Status = Narrative.NarrativeStatus.Generated;
            poco.Text.Div = $"<div>{text}</div>";
            return poco.ToResourceElement();
        }

        public static ResourceElement UpdatePatientFamilyName(this ResourceElement resource, string familyName)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            var poco = resource.ToPoco<Patient>();

            if (poco.Name == null)
            {
                poco.Name = new List<HumanName>();
            }

            poco.Name.Add(new HumanName() { Family = familyName });
            return poco.ToResourceElement();
        }

        public static ResourceElement UpdatePatientAddressCity(this ResourceElement resource, string city)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            var poco = resource.ToPoco<Patient>();

            if (poco.Address == null)
            {
                poco.Address = new List<Address>();
            }

            poco.Address.Add(new Address() { City = city });
            return poco.ToResourceElement();
        }

        public static ResourceElement UpdatePatientGender(this ResourceElement resource, string gender)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            var poco = resource.ToPoco<Patient>();

            if (string.IsNullOrWhiteSpace(gender))
            {
                poco.Gender = null;
            }
            else
            {
                poco.Gender = gender.GetValueByEnumLiteral<AdministrativeGender>();
            }

            return poco.ToResourceElement();
        }

        public static ResourceElement UpdateObservationStatus(this ResourceElement resource, string status)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            var poco = resource.ToPoco<Observation>();

            poco.Status = status.GetValueByEnumLiteral<ObservationStatus>();

            return poco.ToResourceElement();
        }

        public static ResourceElement UpdateObservationValueQuantity(this ResourceElement resource, decimal quantity, string unit, string system)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            var poco = resource.ToPoco<Observation>();

            poco.Value = new Quantity(quantity, unit, system);

            return poco.ToResourceElement();
        }

        public static ResourceElement UpdateObservationValueCodeableConcept(this ResourceElement resource, string system, string code, string text, (string system, string code, string display)[] codings)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            var poco = resource.ToPoco<Observation>();

            var value = new CodeableConcept(system, code, text);

            if (codings != null)
            {
                value.Coding = codings.Select(x => new Coding(x.system, x.code, x.display)).ToList();
            }

            poco.Value = value;

            return poco.ToResourceElement();
        }

        public static ResourceElement UpdateObservationEffectiveDate(this ResourceElement resource, string date)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            var poco = resource.ToPoco<Observation>();

            poco.Effective = new FhirDateTime(date);

            return poco.ToResourceElement();
        }

        public static ResourceElement AddObservationCoding(this ResourceElement resource, string system, string code)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            var poco = resource.ToPoco<Observation>();

            if (poco?.Code == null)
            {
                poco.Code = new CodeableConcept();
            }

            if (poco.Code.Coding == null)
            {
                poco.Code.Coding = new List<Coding>();
            }

            poco.Code.Coding.Add(new Coding(system, code));

            return poco.ToResourceElement();
        }

        public static ResourceElement AddMetaTag(this ResourceElement resource, string system, string code)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            var poco = resource.ToPoco();

            if (poco?.Meta == null)
            {
                poco.Meta = new Meta();
            }

            if (poco.Meta.Tag == null)
            {
                poco.Meta.Tag = new List<Coding>();
            }

            poco.Meta.Tag.Add(new Coding(system, code));

            return poco.ToResourceElement();
        }

        public static ResourceElement AddObservationIdentifier(this ResourceElement resource, string system, string value)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            var poco = resource.ToPoco<Observation>();

            if (poco.Identifier == null)
            {
                poco.Identifier = new List<Identifier>();
            }

            poco.Identifier.Add(new Identifier(system, value));

            return poco.ToResourceElement();
        }

        public static ResourceElement AddDocumentReferenceIdentifier(this ResourceElement resource, string system, string value)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            var poco = resource.ToPoco<DocumentReference>();

            if (poco.Identifier == null)
            {
                poco.Identifier = new List<Identifier>();
            }

            poco.Identifier.Add(new Identifier(system, value));

            return poco.ToResourceElement();
        }

        public static ResourceElement UpdateValueSetStatus(this ResourceElement resource, string status)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            var poco = resource.ToPoco<ValueSet>();

            poco.Status = status.GetValueByEnumLiteral<PublicationStatus>();

            return poco.ToResourceElement();
        }

        public static ResourceElement UpdateValueSetUrl(this ResourceElement resource, string url)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            var poco = resource.ToPoco<ValueSet>();

            poco.Url = url;

            return poco.ToResourceElement();
        }

        public static ResourceElement UpdateRiskAssessmentSubject(this ResourceElement resource, string reference)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            var poco = resource.ToPoco<RiskAssessment>();

            poco.Subject = new ResourceReference(reference);

            return poco.ToResourceElement();
        }

        public static ResourceElement UpdateRiskAssessmentStatus(this ResourceElement resource, string status)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            var poco = resource.ToPoco<RiskAssessment>();

            poco.Status = status.GetValueByEnumLiteral<ObservationStatus>();

            return poco.ToResourceElement();
        }

        public static ResourceElement UpdateRiskAssessmentProbability(this ResourceElement resource, int probability)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            var poco = resource.ToPoco<RiskAssessment>();

            if (poco.Prediction == null)
            {
                poco.Prediction = new List<RiskAssessment.PredictionComponent>();
            }

            poco.Prediction.Add(new RiskAssessment.PredictionComponent { Probability = new FhirDecimal(probability) });

            return poco.ToResourceElement();
        }

        public static ResourceElement UpdatePatientManagingOrganization(this ResourceElement resource, string reference)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            var poco = resource.ToPoco<Patient>();

            poco.ManagingOrganization = new ResourceReference(reference);

            return poco.ToResourceElement();
        }

        public static ResourceElement UpdateObservationSubject(this ResourceElement resource, string reference)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            var poco = resource.ToPoco<Observation>();

            poco.Subject = new ResourceReference(reference);

            return poco.ToResourceElement();
        }

        public static ResourceElement UpdateEncounterSubject(this ResourceElement resource, string reference)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            var poco = resource.ToPoco<Encounter>();

            poco.Subject = new ResourceReference(reference);

            return poco.ToResourceElement();
        }

        public static ResourceElement UpdateConditionSubject(this ResourceElement resource, string reference)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            var poco = resource.ToPoco<Condition>();

            poco.Subject = new ResourceReference(reference);

            return poco.ToResourceElement();
        }

        public static ResourceElement UpdateObservationDevice(this ResourceElement resource, string reference)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            var poco = resource.ToPoco<Observation>();

            poco.Device = new ResourceReference(reference);

            return poco.ToResourceElement();
        }

        public static SearchParameterInfo ToInfo(this SearchParameter searchParam)
        {
            EnsureArg.IsNotNull(searchParam, nameof(searchParam));

            return new SearchParameterInfo(
                searchParam.Name,
                string.IsNullOrEmpty(searchParam.Url) ? null : new Uri(searchParam.Url),
                searchParam.Type?.ToString(),
                searchParam.Component?.Select(x => new SearchParameterComponentInfo(x.GetComponentDefinitionUri(), x.Expression)).ToArray(),
                searchParam.Expression,
                searchParam.Target?.Select(x => x?.ToString()).ToArray());
        }

        public static ValueSets.SearchParamType ToValueSet(this SearchParamType searchParam)
        {
            return Enum.Parse<ValueSets.SearchParamType>(searchParam.ToString());
        }
    }
}
