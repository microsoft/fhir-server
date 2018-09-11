// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.Legacy.SearchValues
{
    public class TestResource : Resource
    {
        public const string ExpectedSystem1 = "system1";
        public const string ExpectedCode1 = "code1";
        public const string ExpectedSystem2 = "system2";
        public const string ExpectedCode2 = "code2";
        public const string ExpectedSystem3 = "system3";
        public const string ExpectedCode3 = "code3";
        public const string ExpectedQuantitySystem = "qSystem";
        public const string ExpectedQuantityCode = "qCode";
        public const decimal ExpectedQuantityValue = 1.345m;
        public const string ExpectedReference = "Patient\\123";
        public const string ExpectedString = "test";
        public const decimal ExpectedDecimal = 1.345m;

        public static readonly DateTimeOffset ExpectedDateTimeStart = new DateTimeOffset(2017, 1, 1, 0, 0, 0, TimeSpan.FromMinutes(0));
        public static readonly DateTimeOffset ExpectedDateTimeEnd = new DateTimeOffset(2017, 1, 1, 23, 59, 59, 999, TimeSpan.FromMinutes(0)).AddTicks(9999);

        private ResourceReference _reference = new ResourceReference() { Reference = ExpectedReference };

        public CodeableConcept SingleCodeableConcept
        {
            get
            {
                CodeableConcept code = new CodeableConcept();

                code.Coding.Add(new Coding(ExpectedSystem1, ExpectedCode1));

                return code;
            }
        }

        public CodeableConcept MultipleCodeableConcept
        {
            get
            {
                CodeableConcept code = new CodeableConcept();

                code.Coding.Add(new Coding(ExpectedSystem2, ExpectedCode2));
                code.Coding.Add(new Coding(ExpectedSystem3, ExpectedCode3));

                return code;
            }
        }

        public decimal? Decimal => ExpectedDecimal;

        public FhirDateTime FhirDateTime => new FhirDateTime(2017, 1, 1);

        public FhirString FhirString => new FhirString(ExpectedString);

        public Quantity Quantity
        {
            get
            {
                return new Quantity(ExpectedQuantityValue, ExpectedQuantityCode, ExpectedQuantitySystem);
            }
        }

        public override ResourceType ResourceType => ResourceType.Resource;

        public ResourceReference ResourceReference
        {
            get { return _reference; }
            set { _reference = value; }
        }

        public override IDeepCopyable DeepCopy()
        {
            // Not being used.
            return null;
        }
    }
}
