// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Net.Http;
using EnsureThat;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Core.Features.Routing;

namespace Microsoft.Health.Fhir.Core.Features.Subscriptions
{
    public class RestHookNotificationChannel : INotificationChannel
    {
        private IHttpClientFactory _httpClientFactory;
        private IUrlResolver _urlResolver;
        private FhirJsonSerializer _fhirJsonSerializer = new FhirJsonSerializer();

        public RestHookNotificationChannel(IHttpClientFactory httpClientFactory, IUrlResolver urlResolver)
        {
            EnsureArg.IsNotNull(httpClientFactory, nameof(httpClientFactory));
            EnsureArg.IsNotNull(urlResolver, nameof(urlResolver));

            _httpClientFactory = httpClientFactory;
            _urlResolver = urlResolver;
        }

        public async void NotifiyEmpty(Subscription subscription)
        {
            using (var httpClient = _httpClientFactory.CreateClient())
            {
                Bundle bundle = new Bundle();
                bundle.Type = Bundle.BundleType.SubscriptionNotification;

                using (var content = new StringContent(_fhirJsonSerializer.SerializeToString(bundle)))
                {
                    await httpClient.PostAsync(new Uri(subscription.Endpoint), content);
                }
            }
        }

        public async void NotifiyIdOnly(Subscription subscription, Resource[] resources)
        {
            // TODO: Add body
            using (var httpClient = _httpClientFactory.CreateClient())
            {
                Bundle bundle = new Bundle();
                bundle.Type = Bundle.BundleType.SubscriptionNotification;
                bundle.Entry = new List<Bundle.EntryComponent>();
                foreach (var resource in resources)
                {
                    var uri = _urlResolver.ResolveResourceUrl(resource.Id, resource.TypeName, resource.VersionId, false).ToString();
                    bundle.Entry.Add(new Bundle.EntryComponent()
                    {
                        FullUrl = uri,
                        Request = new Bundle.RequestComponent()
                        {
                            // This is not correct. The verb should be the verb that triggered the notification
                            // (Post, Put, Delete) for the primary resource and Get for associated resources.
                            Method = Bundle.HTTPVerb.GET,
                            Url = uri,
                        },
                        Response = new Bundle.ResponseComponent()
                        {
                            // This is not correct. The status should be the status of the request that triggered
                            // the notification for the primary resource and 200 for associated resources.
                            Status = "200",
                        },
                    });
                }

                using (var content = new StringContent(_fhirJsonSerializer.SerializeToString(bundle)))
                {
                    await httpClient.PostAsync(new Uri(subscription.Endpoint), content);
                }
            }
        }

        public async void NotifiyFullResource(Subscription subscription, Resource[] resources)
        {
            using (var httpClient = _httpClientFactory.CreateClient())
            {
                Bundle bundle = new Bundle();
                bundle.Type = Bundle.BundleType.SubscriptionNotification;
                bundle.Entry = new List<Bundle.EntryComponent>();
                foreach (var resource in resources)
                {
                    var uri = _urlResolver.ResolveResourceUrl(resource.Id, resource.TypeName, resource.VersionId, false).ToString();
                    bundle.Entry.Add(new Bundle.EntryComponent()
                    {
                        FullUrl = uri,
                        Resource = resource,
                        Request = new Bundle.RequestComponent()
                        {
                            // This is not correct. The verb should be the verb that triggered the notification
                            // (Post, Put, Delete) for the primary resource and Get for associated resources.
                            Method = Bundle.HTTPVerb.GET,
                            Url = uri,
                        },
                        Response = new Bundle.ResponseComponent()
                        {
                            // This is not correct. The status should be the status of the request that triggered
                            // the notification for the primary resource and 200 for associated resources.
                            Status = "200",
                        },
                    });
                }

                using (var content = new StringContent(_fhirJsonSerializer.SerializeToString(bundle)))
                {
                    await httpClient.PostAsync(new Uri(subscription.Endpoint), content);
                }
            }
        }
    }
}
