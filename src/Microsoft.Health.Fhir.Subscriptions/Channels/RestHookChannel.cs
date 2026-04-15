// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Subscriptions.Models;
using Microsoft.Health.Fhir.Subscriptions.Validation;

namespace Microsoft.Health.Fhir.Subscriptions.Channels
{
    [ChannelType(SubscriptionChannelType.RestHook)]
    public sealed class RestHookChannel : ISubscriptionChannel, IDisposable
    {
        private readonly ILogger<RestHookChannel> _logger;
        private readonly IBundleFactory _bundleFactory;
        private readonly IRawResourceFactory _rawResourceFactory;
        private readonly HttpClient _httpClient;
        private readonly IModelInfoProvider _modelInfoProvider;
        private readonly IUrlResolver _urlResolver;
        private RequestContextAccessor<IFhirRequestContext> _contextAccessor;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IActionContextAccessor _actionContextAccessor;

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "HttpClientHandler is disposed by HttpClient.")]
        public RestHookChannel(
            ILogger<RestHookChannel> logger,
            IBundleFactory bundleFactory,
            IRawResourceFactory rawResourceFactory,
            IModelInfoProvider modelInfoProvider,
            IUrlResolver urlResolver,
            RequestContextAccessor<IFhirRequestContext> contextAccessor,
            IHttpContextAccessor httpContextAccessor,
            IActionContextAccessor actionContextAccessor)
        {
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
            _bundleFactory = EnsureArg.IsNotNull(bundleFactory, nameof(bundleFactory));
            _rawResourceFactory = EnsureArg.IsNotNull(rawResourceFactory, nameof(rawResourceFactory));
            _modelInfoProvider = EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));
            _urlResolver = EnsureArg.IsNotNull(urlResolver, nameof(urlResolver));

            _contextAccessor = EnsureArg.IsNotNull(contextAccessor, nameof(contextAccessor));
            _httpContextAccessor = EnsureArg.IsNotNull(httpContextAccessor, nameof(httpContextAccessor));
            _actionContextAccessor = EnsureArg.IsNotNull(actionContextAccessor, nameof(actionContextAccessor));

            var handler = new HttpClientHandler() { CheckCertificateRevocationList = true };
            _httpClient = new HttpClient(handler, disposeHandler: true);
        }

        public async Task PublishAsync(IReadOnlyCollection<ResourceWrapper> resources, SubscriptionInfo subscriptionInfo, DateTimeOffset transactionTime, CancellationToken cancellationToken)
        {
            SetContext();
            List<ResourceWrapper> resourceWrappers = new List<ResourceWrapper>();
            var parameter = new Parameters
            {
                { "subscription", new ResourceReference(subscriptionInfo.ResourceId) },
                { "topic", new FhirUri(subscriptionInfo.Topic) },
                { "status", new Code(subscriptionInfo.Status.ToString()) },
                { "type", new Code("event-notification") },
            };

            if (subscriptionInfo.Channel.ContentType != SubscriptionContentType.Empty)
            {
                // add new fields to parameter object from subscription data
                foreach (ResourceWrapper rw in resources)
                {
                    var notificationEvent = new Parameters.ParameterComponent
                    {
                        Name = "notification-event",
                        Part = new List<Parameters.ParameterComponent>
                        {
                            new Parameters.ParameterComponent
                            {
                                Name = "focus",
                                Value = new FhirUri(_urlResolver.ResolveResourceWrapperUrl(rw)),
                            },
                            new Parameters.ParameterComponent
                            {
                                Name = "timestamp",
                                Value = new FhirDateTime(transactionTime),
                            },
                        },
                    };
                    parameter.Parameter.Add(notificationEvent);
                }
            }

            parameter.Id = Guid.NewGuid().ToString();
            var parameterResourceElement = _modelInfoProvider.ToResourceElement(parameter);
            var parameterResourceWrapper = new ResourceWrapper(parameterResourceElement, _rawResourceFactory.Create(parameterResourceElement, keepMeta: true), new ResourceRequest(HttpMethod.Post, "http://fhir"), false, new List<SearchIndexEntry>().AsReadOnly(), new CompartmentIndices(), new List<KeyValuePair<string, string>>().AsReadOnly());
            resourceWrappers.Add(parameterResourceWrapper);

            if (subscriptionInfo.Channel.ContentType.Equals(SubscriptionContentType.FullResource))
            {
                resourceWrappers.AddRange(resources);
            }

            string bundle = await _bundleFactory.CreateSubscriptionBundleAsync(resourceWrappers.ToArray());

            await SendPayload(subscriptionInfo.Channel, bundle, cancellationToken);
        }

        public async Task PublishHandShakeAsync(SubscriptionInfo subscriptionInfo, CancellationToken cancellationToken)
        {
            List<ResourceWrapper> resourceWrappers = new List<ResourceWrapper>();
            var parameter = new Parameters
            {
                { "subscription", new ResourceReference(subscriptionInfo.ResourceId) },
                { "topic", new FhirUri(subscriptionInfo.Topic) },
                { "status", new Code(subscriptionInfo.Status.ToString()) },
                { "type", new Code("handshake") },
            };

            parameter.Id = Guid.NewGuid().ToString();
            var parameterResourceElement = _modelInfoProvider.ToResourceElement(parameter);
            var parameterResourceWrapper = new ResourceWrapper(parameterResourceElement, _rawResourceFactory.Create(parameterResourceElement, keepMeta: true), new ResourceRequest(HttpMethod.Post, "http://fhir"), false, null, null, null);
            resourceWrappers.Add(parameterResourceWrapper);

            string bundle = await _bundleFactory.CreateSubscriptionBundleAsync(resourceWrappers.ToArray());

            await SendPayload(subscriptionInfo.Channel, bundle, cancellationToken);
        }

        public async Task PublishHeartBeatAsync(SubscriptionInfo subscriptionInfo, CancellationToken cancellationToken)
        {
            SetContext();
            List<ResourceWrapper> resourceWrappers = new List<ResourceWrapper>();
            var parameter = new Parameters
            {
                { "subscription", new ResourceReference(subscriptionInfo.ResourceId) },
                { "topic", new FhirUri(subscriptionInfo.Topic) },
                { "status", new Code(subscriptionInfo.Status.ToString()) },
                { "type", new Code("heartbeat") },
            };

            parameter.Id = Guid.NewGuid().ToString();
            var parameterResourceElement = _modelInfoProvider.ToResourceElement(parameter);
            var parameterResourceWrapper = new ResourceWrapper(parameterResourceElement, _rawResourceFactory.Create(parameterResourceElement, keepMeta: true), new ResourceRequest(HttpMethod.Post, "http://fhir"), false, null, null, null);
            resourceWrappers.Add(parameterResourceWrapper);

            string bundle = await _bundleFactory.CreateSubscriptionBundleAsync(resourceWrappers.ToArray());

            await SendPayload(subscriptionInfo.Channel, bundle, cancellationToken);
        }

        private void SetContext()
        {
            if (_httpContextAccessor.HttpContext == null)
            {
                var fhirRequestContext = new FhirRequestContext(
                    method: "subscription",
                    uriString: "subscription",
                    baseUriString: "subscription",
                    correlationId: "subscription",
                    requestHeaders: new Dictionary<string, StringValues>(),
                    responseHeaders: new Dictionary<string, StringValues>())
                {
                    IsBackgroundTask = true,
                    AuditEventType = OperationsConstants.Subscription,
                };
                _contextAccessor.RequestContext = fhirRequestContext;

                var httpContext = new DefaultHttpContext();
                httpContext.Request.Scheme = "https";
                httpContext.Request.Host = new HostString("fhir.azurehealthcareapis.com", 433);
                _httpContextAccessor.HttpContext = httpContext;

                _actionContextAccessor.ActionContext = new AspNetCore.Mvc.ActionContext();
                _actionContextAccessor.ActionContext.HttpContext = httpContext;
            }
        }

        private async Task SendPayload(
        ChannelInfo chanelInfo,
        string contents,
        CancellationToken cancellationToken)
        {
            HttpRequestMessage request = null!;

            // send the request to the endpoint
            try
            {
                // build our request
                request = new HttpRequestMessage()
                {
                    Method = HttpMethod.Post,
                    RequestUri = new Uri(chanelInfo.Endpoint),
                    Content = new StringContent(contents),
                };

                // send our request
                HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);

                // check the status code
                if ((response.StatusCode != System.Net.HttpStatusCode.OK) &&
                    (response.StatusCode != System.Net.HttpStatusCode.Accepted))
                {
                    // failure
                   _logger.LogError($"REST POST to {chanelInfo.Endpoint} failed: {response.StatusCode}");
                   throw new SubscriptionException(Resources.SubscriptionInvalid);
                }
                else
                {
                    _logger.LogError($"REST POST to {chanelInfo.Endpoint} succeeded: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"REST POST {chanelInfo.ChannelType} to {chanelInfo.Endpoint} failed: {ex.Message}");
            }
            finally
            {
                if (request != null)
                {
                    request.Dispose();
                }
            }
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}
