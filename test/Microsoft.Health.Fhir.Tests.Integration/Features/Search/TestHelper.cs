// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Search.Legacy.SearchValues;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.Integration.Features.Search
{
    public static class TestHelper
    {
        public const string DateTime1 = "2007";

        public const string DateTime2 = "2017-01-31T12:00:00";

        public const string ActivityDefinitionReference = @"ActivityDefinition\ad1";

        public const string EncounterReference = @"Encounter\e1";

        public const string LocationReference = @"Location\def";

        public const decimal Number1 = 100;

        public const decimal Number2 = 100.00m;

        public const string ObservationReference = @"Observation\ob1";

        public const string OrganizationReference = @"Organization\or1";

        public const string PatientReference = @"Patient\123";

        public const string PlanDefinitionReference = @"PlanDefinition\pd1";

        public const string PractitionerReference = @"Practitioner\p1";

        public const string QuestionnaireReference = @"Questionnaire\q1";

        public const string ReferralRequestReference1 = @"ReferralRequest\rr1";

        public const string ReferralRequestReference2 = @"ReferralRequest\rr2";

        public const string String1 = "Test";

        public const string String2 = "Health";

        public const string String3 = "Fhir";

        public const string Url1 = "http://halth";

        public const string Url2 = "http://uri";

        public static Age Age1 => new Age
        {
            Value = 18,
        };

        public static FhirBoolean FhirBooleanTrue => new FhirBoolean(true);

        public static FhirBoolean FhirBooleanFalse => new FhirBoolean(false);

        public static DateTimeOffset Instant1 => new DateTimeOffset(2007, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public static Coding CodingTrue => new Coding(string.Empty, "true", null);

        public static Coding Coding1WithText => new Coding("system1", "code1", "display1");

        public static Coding Coding2 => new Coding("system2", "code2");

        public static Coding Coding3WithText => new Coding("system3", "code3", "display3");

        public static Coding Coding4 => new Coding("system4", "code4");

        public static Coding Coding5 => new Coding("system5", "code5");

        public static Coding Coding6WithText => new Coding("system6", "code6", "display6");

        public static Coding CodingForCodeableConcept1WithText => new Coding(null, null, CodeableConcept1WithText.Text);

        public static Coding CodingForCodeableConcept3WithText => new Coding(null, null, CodeableConcept3WithText.Text);

        public static CodeableConcept CodeableConcept1WithText
        {
            get
            {
                var cc = new CodeableConcept
                {
                    Text = "CodeableConcept1",
                };

                cc.Coding.Add(Coding1WithText);
                cc.Coding.Add(Coding2);

                return cc;
            }
        }

        public static CodeableConcept CodeableConcept2
        {
            get
            {
                var cc = new CodeableConcept();

                cc.Coding.Add(Coding3WithText);
                cc.Coding.Add(Coding4);

                return cc;
            }
        }

        public static CodeableConcept CodeableConcept3WithText => new CodeableConcept(
            Coding5.System, Coding5.Code, Coding5.Display, "CodeableConcept3");

        public static CodeableConcept CodeableConcept4 => new CodeableConcept(
            Coding6WithText.System, Coding6WithText.Code, Coding6WithText.Display);

        public static IEnumerable<Coding> CodingsForCodeableConcept1WithText => new[]
        {
            CodingForCodeableConcept1WithText,
            Coding1WithText,
            Coding2,
        };

        public static IEnumerable<Coding> CodingsForCodeableConcept2 => new[]
        {
            Coding3WithText,
            Coding4,
        };

        public static IEnumerable<Coding> CodingsForCodeableConcept3WithText => new[]
        {
            CodingForCodeableConcept3WithText,
            Coding5,
        };

        public static IEnumerable<Coding> CodingsForCodeableConcept4 => new[]
        {
            Coding6WithText,
        };

        public static FhirDateTime FhirDateTime1 => new FhirDateTime(DateTime1);

        public static FhirString FhirString1 => new FhirString(String1);

        public static Period Period1 => new Period(new FhirDateTime(2018, 1, 1), new FhirDateTime(2018, 12, 31));

        public static Quantity Quantity1 => new Quantity(1.3m, "qCode1", "qSystem1");

        public static Quantity Quantity2 => new Quantity(400m, "qCode2", "qSystem2");

        public static Quantity Quantity3 => new Quantity(0m, "qCode3", "qSystem3");

        public static Range Range1 => new Range { Low = new Quantity(0, "in"), High = new Quantity(12, "in") };

        public static CodeableConcept CreateCodeableConcept(string text, params Coding[] codings)
        {
            var concept = new CodeableConcept();

            concept.Text = text;
            concept.Coding.AddRange(codings);

            return concept;
        }

        public static CodeableConcept CreateCodeableConcept(params Coding[] codings)
        {
            return CreateCodeableConcept(null, codings);
        }

        public static void ValidateComposite<TValue>(Coding expectedCoding, TValue expectedValue, Action<TValue, ISearchValue> valueValidator, ISearchValue sv)
        {
            LegacyCompositeSearchValue csv = Assert.IsType<LegacyCompositeSearchValue>(sv);

            Assert.Equal(expectedCoding.System, csv.System);
            Assert.Equal(expectedCoding.Code, csv.Code);

            valueValidator(expectedValue, csv.Value);
        }

        public static void ValidateToken(Coding expected, ISearchValue sv)
        {
            ValidateTokenInternal(expected, sv);
        }

        public static void ValidateTokenWithText(Coding expected, ISearchValue sv)
        {
            TokenSearchValue tsv = ValidateTokenInternal(expected, sv);

            Assert.Equal(expected.Display, tsv.Text);
        }

        private static TokenSearchValue ValidateTokenInternal(Coding expected, ISearchValue sv)
        {
            TokenSearchValue tsv = Assert.IsType<TokenSearchValue>(sv);

            Assert.Equal(expected.System, tsv.System);
            Assert.Equal(expected.Code, tsv.Code);

            return tsv;
        }
    }
}
