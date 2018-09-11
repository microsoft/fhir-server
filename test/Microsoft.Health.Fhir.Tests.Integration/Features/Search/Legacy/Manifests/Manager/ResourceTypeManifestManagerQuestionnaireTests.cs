// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Hl7.Fhir.Model;
using Xunit;
using static Microsoft.Health.Fhir.Tests.Common.Search.SearchValueValidationHelper;
using static Microsoft.Health.Fhir.Tests.Integration.Features.Search.TestHelper;

namespace Microsoft.Health.Fhir.Tests.Integration.Features.Search.Legacy.Manifests
{
    public class ResourceTypeManifestManagerQuestionnaireTests : ResourceTypeManifestManagerTests<Questionnaire>
    {
        private readonly Questionnaire _questionnaire = new Questionnaire();

        protected override Questionnaire Resource => _questionnaire;

        [Fact]
        public void GivenAQuestionnaireWithCode_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestTokenSearchParam(
                "code",
                () =>
                {
                    _questionnaire.Item = new List<Questionnaire.ItemComponent>
                    {
                        new Questionnaire.ItemComponent { Code = new List<Coding> { Coding1WithText } },
                    };
                },
                Coding1WithText);
        }

        [Fact]
        public void GivenAQuestionnaireWithDate_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "date",
                () => { _questionnaire.Date = DateTime1; },
                ValidateDateTime,
                DateTime1);
        }

        [Fact]
        public void GivenAQuestionnaireWithDescription_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "description",
                () => { _questionnaire.Description = new Markdown(String1); },
                ValidateString,
                String1);
        }

        [Fact]
        public void GivenAQuestionnaireWithEffectivePeriod_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "effective",
                () => { _questionnaire.EffectivePeriod = Period1; },
                ValidateDateTime,
                "2018");
        }

        [Fact]
        public void GivenAQuestionnaireWithIdentifier_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestIdentifier(a => a.Identifier);
        }

        [Fact]
        public void GivenAQuestionnaireWithJurisdiction_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestTokenSearchParam(
                "jurisdiction",
                () => { _questionnaire.Jurisdiction = new List<CodeableConcept> { CodeableConcept1WithText }; },
                CodingsForCodeableConcept1WithText);
        }

        [Fact]
        public void GivenAQuestionnaireWithName_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "name",
                () => { _questionnaire.Name = String1; },
                ValidateString,
                String1);
        }

        [Fact]
        public void GivenAQuestionnaireWithPublisher_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "publisher",
                () => { _questionnaire.Publisher = String1; },
                ValidateString,
                String1);
        }

        [Fact]
        public void GivenAQuestionnaireWithStatus_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestTokenSearchParam(
                "status",
                () =>
                {
                    _questionnaire.Status = PublicationStatus.Active;
                },
                new Coding("http://hl7.org/fhir/publication-status", "active"));
        }

        [Fact]
        public void GivenAQuestionnaireWithTitle_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "title",
                () => { _questionnaire.Title = String1; },
                ValidateString,
                String1);
        }

        [Fact]
        public void GivenAQuestionnaireWithUrl_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "url",
                () => { _questionnaire.Url = Url1; },
                ValidateUri,
                Url1);
        }

        [Fact]
        public void GivenAQuestionnaireWithVersion_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestVersion(q => q.Version);
        }
    }
}
