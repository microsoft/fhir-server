// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using BenchmarkDotNet.Attributes;
using FluentValidation;
using FluentValidation.Internal;
using FluentValidation.Validators;
using Hl7.Fhir.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Core.Features.Validation.Narratives;
using Microsoft.Health.Fhir.Tests.Common;

namespace Microsoft.Health.Fhir.Core.UnitTests.Performance
{
    [InProcess]
    public class ValidationPerformanceTests
    {
        private static readonly Patient Resource;
        private static readonly NarrativeValidator Validator;

        static ValidationPerformanceTests()
        {
            Validator = new NarrativeValidator(new NarrativeHtmlSanitizer(NullLogger<NarrativeHtmlSanitizer>.Instance));

            Resource = Samples.GetDefaultPatient();
        }

        [Benchmark]
        public void ValidateHtml()
        {
            Validator.Validate(
                new PropertyValidatorContext(
                    new ValidationContext(Resource),
                    PropertyRule.Create<Observation, Resource>(x => x),
                    "Resource"));
        }
    }
}
