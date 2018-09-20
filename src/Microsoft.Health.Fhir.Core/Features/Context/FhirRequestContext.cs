// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Security.Claims;
using System.Threading;
using EnsureThat;
using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Core.Features.Context
{
    public class FhirRequestContext : IFhirRequestContext
    {
        private Uri _uri;
        private Uri _baseUri;

        public FhirRequestContext(
            string method,
            string scheme,
            string host,
            int? port,
            string pathBase,
            string path,
            string queryString,
            Coding requestType,
            string correlationId)
        {
            EnsureArg.IsNotNullOrWhiteSpace(method, nameof(method));
            EnsureArg.IsNotNullOrWhiteSpace(scheme, nameof(scheme));
            EnsureArg.IsNotNullOrWhiteSpace(host, nameof(host));
            EnsureArg.IsNotNullOrWhiteSpace(path, nameof(path));
            EnsureArg.IsNotNull(requestType, nameof(requestType));
            EnsureArg.IsNotNullOrWhiteSpace(correlationId, nameof(correlationId));

            Method = method;
            Scheme = scheme;
            Host = host;
            Port = port;
            PathBase = pathBase;
            Path = path;
            QueryString = queryString;
            RequestType = requestType;
            CorrelationId = correlationId;
        }

        public string Method { get; }

        public string Scheme { get; }

        public string Host { get; }

        public int? Port { get; }

        public string PathBase { get; }

        public string Path { get; }

        public string QueryString { get; }

        public Uri Uri
        {
            get
            {
                LazyInitializer.EnsureInitialized(ref _uri, () =>
                {
                    var builder = new UriBuilder
                    {
                        Scheme = Scheme,
                        Host = Host,
                        Path = $"{PathBase}{Path}",
                        Query = QueryString,
                    };

                    if (Port != null)
                    {
                        builder.Port = Port.Value;
                    }

                    return builder.Uri;
                });

                return _uri;
            }
        }

        public Uri BaseUri
        {
            get
            {
                LazyInitializer.EnsureInitialized(ref _baseUri, () =>
                {
                    var builder = new UriBuilder
                    {
                        Scheme = Scheme,
                        Host = Host,
                        Path = PathBase,
                    };

                    if (Port != null)
                    {
                        builder.Port = Port.Value;
                    }

                    return builder.Uri;
                });

                return _baseUri;
            }
        }

        public string CorrelationId { get; }

        public Coding RequestType { get; }

        public Coding RequestSubType { get; set; }

        public string RouteName { get; set; }

        public ClaimsPrincipal Principal { get; set; }
    }
}
