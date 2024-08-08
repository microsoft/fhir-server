// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using FluentValidation.Results;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Subscriptions.Models;

namespace Microsoft.Health.Fhir.Subscriptions.Validation
{
    public class SubscriptionValidator : ISubscriptionValidator
    {
        private readonly ILogger _logger;
        private readonly ISubscriptionModelConverter _subscriptionModelConverter;

        public SubscriptionValidator(ILogger logger, ISubscriptionModelConverter subscriptionModelConverter)
        {
            _logger = logger;
            _subscriptionModelConverter = subscriptionModelConverter;
        }

        public SubscriptionInfo ValidateSubscriptionInput(ResourceElement subscription, CancellationToken cancellationToken)
        {
            SubscriptionInfo subscriptionInfo = _subscriptionModelConverter.Convert(subscription);

            var validationFailures = new List<ValidationFailure>();

            if (subscriptionInfo.Channel.ChannelType.Equals(SubscriptionChannelType.None))
            {
                _logger.LogInformation("Subscription channel type is not valid.");
                validationFailures.Add(
                    new ValidationFailure(nameof(subscriptionInfo.Channel.ChannelType), "Subscription channel type is not valid."));
            }

            if (validationFailures.Any())
            {
                throw new ResourceNotValidException(validationFailures);
            }

            return subscriptionInfo;
        }
    }
}
