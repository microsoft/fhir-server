// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Reflection;
using FluentValidation;
using FluentValidation.Results;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Support;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features;

[Trait(Traits.OwningTeam, OwningTeam.Fhir)]
[Trait(Traits.Category, Categories.Validate)]
public class ResourceProfileValidatorTests
{
    private readonly ResourceProfileValidator _validator;
    private readonly IProfileValidator _profileValidator;
    private readonly IModelAttributeValidator _modelAttributeValidator;
    private readonly IFhirRequestContext _fhirRequestContext;
    private readonly OperationOutcomeIssue _operationOutcomeWarningIssue = new(OperationOutcomeConstants.IssueSeverity.Warning, OperationOutcomeConstants.IssueType.Invalid, detailsText: "Warning text.");
    private readonly Dictionary<string, StringValues> _strictHeader = new Dictionary<string, StringValues> { { "Prefer", "handling=strict" } };
    private readonly ResourceElement _sample = Samples.GetJsonSample("Profile-Patient-uscore");

    public ResourceProfileValidatorTests()
    {
        RequestContextAccessor<IFhirRequestContext> requestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
        _fhirRequestContext = Substitute.For<IFhirRequestContext>();
        requestContextAccessor.RequestContext.Returns(_fhirRequestContext);

        _profileValidator = Substitute.For<IProfileValidator>();
        _modelAttributeValidator = Substitute.For<IModelAttributeValidator>();
        _validator = new ResourceProfileValidator(
            _modelAttributeValidator,
            _profileValidator,
            requestContextAccessor,
            true);
    }

    [Fact]
    public void GivenAResource_WhenValidatingNoIssues_ThenTheValidationResultSucceeds()
    {
        _profileValidator.TryValidate(Arg.Any<ITypedElement>(), Arg.Any<string>())
            .Returns(Array.Empty<OperationOutcomeIssue>());

        ValidationResult result = _validator.Validate(_sample);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void GivenAResource_WhenValidatingWithAWarning_ThenTheValidationResultSucceeds()
    {
        _profileValidator.TryValidate(Arg.Any<ITypedElement>(), Arg.Any<string>())
            .Returns(new[] { _operationOutcomeWarningIssue });

        ValidationResult result = _validator.Validate(_sample);
        Assert.True(result.IsValid);
    }

    [Theory]
    [MemberData(nameof(ErrorIssues))]
    public void GivenAResource_WhenValidatingWithAnError_ThenTheValidationResultFails(OperationOutcomeIssue issue)
    {
        _profileValidator.TryValidate(Arg.Any<ITypedElement>(), Arg.Any<string>())
            .Returns(new[] { issue });

        ValidationResult result = _validator.Validate(_sample);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void GivenAResource_WhenValidatingWithAWarningAndStrictHandling_ThenTheValidationResultFails()
    {
        _fhirRequestContext.RequestHeaders.Returns(_strictHeader);

        _profileValidator.TryValidate(Arg.Any<ITypedElement>(), Arg.Any<string>())
            .Returns(new[] { _operationOutcomeWarningIssue });

        ValidationResult result = _validator.Validate(_sample);
        Assert.False(result.IsValid);
    }

    [Theory]
    [MemberData(nameof(ErrorIssues))]
    public void GivenAResource_WhenValidatingWithAnErrorAndStrictHandling_ThenTheValidationResultFails(OperationOutcomeIssue issue)
    {
        _fhirRequestContext.RequestHeaders.Returns(_strictHeader);

        _profileValidator.TryValidate(Arg.Any<ITypedElement>(), Arg.Any<string>())
            .Returns(new[] { issue });

        ValidationResult result = _validator.Validate(_sample);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void GivenAResource_WhenValidatingInvalidResource_ThenTheValidationResultHasNoDuplicateErrors()
    {
        ResourceElement resource = Samples.GetJsonSample("ObservationWithNoCode");

        var message = "Element with minimum cardinality 1 cannot be null. At Observation.Code.";
        var memberName = "Code";
        var propertyName = $"Observation.{memberName}";
        _modelAttributeValidator.TryValidate(
            Arg.Any<ResourceElement>(),
            Arg.Do<ICollection<System.ComponentModel.DataAnnotations.ValidationResult>>(
                x =>
                {
                    var result = new System.ComponentModel.DataAnnotations.ValidationResult(message, new string[] { memberName });
                    x.Add(result);
                }),
            Arg.Any<bool>())
            .Returns(false);

        ValidationContext<ResourceElement> context = new ValidationContext<ResourceElement>(resource);
        ValidationResult result = _validator.Validate(context);
        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Equal(Severity.Error, result.Errors[0].Severity);
        Assert.Equal(message, result.Errors[0].ErrorMessage);
        Assert.Equal(propertyName, result.Errors[0].PropertyName);

        var failures = GetFailures(context);
        Assert.Single(failures);
        Assert.Equal(Severity.Error, failures[0].Severity);
        Assert.Equal(message, failures[0].ErrorMessage);
        Assert.Equal(propertyName, failures[0].PropertyName);
    }

    [Fact]
    public void GivenAResource_WhenValidatingInvalidResource_ThenTheValidationResultHasProfileAndContentErrors()
    {
        ResourceElement resource = Samples.GetJsonSample("ObservationWithNoCode");

        var message = "Element with minimum cardinality 1 cannot be null. At Observation.Code.";
        var memberName = "Code";
        var propertyName = $"Observation.{memberName}";
        _modelAttributeValidator.TryValidate(
            Arg.Any<ResourceElement>(),
            Arg.Do<ICollection<System.ComponentModel.DataAnnotations.ValidationResult>>(
                x =>
                {
                    var result = new System.ComponentModel.DataAnnotations.ValidationResult(message, new string[] { memberName });
                    x.Add(result);
                }),
            Arg.Any<bool>())
            .Returns(false);

        var detailedMessage = "Error text.";
        _profileValidator.TryValidate(Arg.Any<ITypedElement>(), Arg.Any<string>())
            .Returns(new[] { new OperationOutcomeIssue(OperationOutcomeConstants.IssueSeverity.Error, OperationOutcomeConstants.IssueType.Invalid, detailsText: detailedMessage) });

        ValidationContext<ResourceElement> context = new ValidationContext<ResourceElement>(resource);
        ValidationResult result = _validator.Validate(context);
        Assert.False(result.IsValid);
        Assert.Equal(2, result.Errors.Count);
        Assert.Contains(
            result.Errors,
            e => e.Severity == Severity.Error && e.ErrorMessage == message && e.PropertyName == propertyName);
        Assert.Contains(
            result.Errors,
            e => e.Severity == Severity.Error && e.ErrorMessage == detailedMessage);

        var failures = GetFailures(context);
        Assert.Equal(2, failures.Count);
        Assert.Contains(
            failures,
            e => e.Severity == Severity.Error && e.ErrorMessage == message && e.PropertyName == propertyName);
        Assert.Contains(
            failures,
            e => e.Severity == Severity.Error && e.ErrorMessage == detailedMessage);
    }

    public static IEnumerable<object[]> ErrorIssues()
    {
        yield return new object[] { new OperationOutcomeIssue(OperationOutcomeConstants.IssueSeverity.Error, OperationOutcomeConstants.IssueType.Invalid, detailsText: "Error text.") };
        yield return new object[] { new OperationOutcomeIssue(OperationOutcomeConstants.IssueSeverity.Fatal, OperationOutcomeConstants.IssueType.Invalid, detailsText: "Fatal text.") };
    }

    private static IList<ValidationFailure> GetFailures(ValidationContext<ResourceElement> context)
    {
        var failures = context?.GetType().GetProperty("Failures", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(context);
        return failures != null ? new List<ValidationFailure>((IList<ValidationFailure>)failures) : new List<ValidationFailure>();
    }
}
