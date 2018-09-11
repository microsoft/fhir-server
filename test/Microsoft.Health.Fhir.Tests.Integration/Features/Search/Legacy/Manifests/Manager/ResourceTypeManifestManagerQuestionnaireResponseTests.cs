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
    public class ResourceTypeManifestManagerQuestionnaireResponseTests : ResourceTypeManifestManagerTests<QuestionnaireResponse>
    {
        private readonly QuestionnaireResponse _questionnaireResponse = new QuestionnaireResponse();

        protected override QuestionnaireResponse Resource => _questionnaireResponse;

        [Fact]
        public void GivenAQuestionnaireWithAuthor_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "author",
                () =>
                {
                    _questionnaireResponse.Author = new ResourceReference(PatientReference);
                },
                ValidateReference,
                PatientReference);
        }

        [Fact]
        public void GivenAQuestionnaireWithDate_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "authored",
                () => { _questionnaireResponse.Authored = DateTime1; },
                ValidateDateTime,
                DateTime1);
        }

        [Fact]
        public void GivenAQuestionnaireWithBasedOn_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "based-on",
                () => { _questionnaireResponse.BasedOn = new List<ResourceReference> { new ResourceReference(ReferralRequestReference1) }; },
                ValidateReference,
                ReferralRequestReference1);
        }

        [Fact]
        public void GivenAQuestionnaireWithContext_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "context",
                () => { _questionnaireResponse.Context = new ResourceReference(EncounterReference); },
                ValidateReference,
                EncounterReference);
        }

        [Fact]
        public void GivenAQuestionnaireResponseWithIdentifier_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestTokenSearchParam(
                "identifier",
                () => { _questionnaireResponse.Identifier = new Identifier(Coding2.System, Coding2.Code); },
                Coding2);
        }

        [Fact]
        public void GivenAQuestionnaireWithParent_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "parent",
                () => { _questionnaireResponse.Parent = new List<ResourceReference> { new ResourceReference(ObservationReference) }; },
                ValidateReference,
                ObservationReference);
        }

        [Fact]
        public void GivenAQuestionnaireWithPatient_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "patient",
                () => { _questionnaireResponse.Subject = new ResourceReference(PatientReference); },
                ValidateReference,
                PatientReference);
        }

        [Fact]
        public void GivenAQuestionnaireWithQuestionnaire_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "questionnaire",
                () => { _questionnaireResponse.Questionnaire = new ResourceReference(QuestionnaireReference); },
                ValidateReference,
                QuestionnaireReference);
        }

        [Fact]
        public void GivenAQuestionnaireWithSource_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "source",
                () => { _questionnaireResponse.Source = new ResourceReference(PatientReference); },
                ValidateReference,
                PatientReference);
        }

        [Fact]
        public void GivenAQuestionnaireWithStatus_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestTokenSearchParam(
                "status",
                () => { _questionnaireResponse.Status = QuestionnaireResponse.QuestionnaireResponseStatus.Completed; },
                new Coding("http://hl7.org/fhir/questionnaire-answers-status", "completed"));
        }

        [Fact]
        public void GivenAQuestionnaireWithSubject_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "subject",
                () => { _questionnaireResponse.Subject = new ResourceReference(PatientReference); },
                ValidateReference,
                PatientReference);
        }
    }
}
