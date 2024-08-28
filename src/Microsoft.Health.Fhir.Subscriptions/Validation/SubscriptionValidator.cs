// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation.Results;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Utility;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Subscriptions;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Subscriptions.Channels;
using Microsoft.Health.Fhir.Subscriptions.Models;

namespace Microsoft.Health.Fhir.Subscriptions.Validation
{
    public class SubscriptionValidator : ISubscriptionValidator
    {
        private readonly ILogger<SubscriptionValidator> _logger;
        private readonly ISubscriptionModelConverter _subscriptionModelConverter;
        private readonly StorageChannelFactory _subscriptionChannelFactory;
        private readonly ISubscriptionUpdator _subscriptionUpdator;

        public SubscriptionValidator(ILogger<SubscriptionValidator> logger, ISubscriptionModelConverter subscriptionModelConverter, StorageChannelFactory subscriptionChannelFactory, ISubscriptionUpdator subscriptionUpdator)
        {
            _logger = logger;
            _subscriptionModelConverter = subscriptionModelConverter;
            _subscriptionChannelFactory = subscriptionChannelFactory;
            _subscriptionUpdator = subscriptionUpdator;
        }

        public async Task<ResourceElement> ValidateSubscriptionInput(ResourceElement subscription, CancellationToken cancellationToken)
        {
            SubscriptionInfo subscriptionInfo = _subscriptionModelConverter.Convert(subscription);

            var validationFailures = new List<ValidationFailure>();

            if (subscriptionInfo.Channel.ChannelType.Equals(SubscriptionChannelType.None))
            {
                _logger.LogInformation("Subscription channel type is not valid.");
                validationFailures.Add(
                    new ValidationFailure(nameof(subscriptionInfo.Channel.ChannelType), "Subscription channel type is not valid."));
            }

            if (!subscriptionInfo.Status.Equals(SubscriptionStatus.Off))
            {
                try
                {
                    var subscriptionChannel = _subscriptionChannelFactory.Create(subscriptionInfo.Channel.ChannelType);
                    await subscriptionChannel.PublishHandShakeAsync(subscriptionInfo);
                    subscription = _subscriptionUpdator.UpdateStatus(subscription, SubscriptionStatus.Active.GetLiteral());
                }
                catch (SubscriptionException)
                {
                    _logger.LogInformation("Subscription endpoint is not valid.");
                    validationFailures.Add(
                        new ValidationFailure(nameof(subscriptionInfo.Channel.Endpoint), "Subscription endpoint is not valid."));
                    subscription = _subscriptionUpdator.UpdateStatus(subscription, SubscriptionStatus.Error.GetLiteral());
                }
            }

            if (validationFailures.Any())
            {
                throw new ResourceNotValidException(validationFailures);
            }

            return subscription;
        }
    }
}
