// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Core.Features.Security
{
    public static class Constants
    {
        private static readonly Coding RestfulSecurityServiceOAuthCodeableConcept = new Coding("http://terminology.hl7.org/CodeSystem/restful-security-service", "OAuth");
        private static readonly Coding RestfulSecurityServiceStu3OAuthCodeableConcept = new Coding("http://hl7.org/fhir/restful-security-service", "OAuth");
        public const string SmartOAuthUriExtension = "http://fhir-registry.smarthealthit.org/StructureDefinition/oauth-uris";
        public const string SmartOAuthUriExtensionToken = "token";
        public const string SmartOAuthUriExtensionAuthorize = "authorize";
        public const string SmartOAuthUriExtensionIntrospection = "introspection";
        public const string SmartOAuthUriExtensionManagement = "management";
        public const string SmartOAuthUriExtensionRevocation = "revocation";

        public const string SmartCapabilitiesUriExtension = "http://hl7.org/fhir/smart-app-launch/StructureDefinition/capabilities";
        public const string ExtensionPropertyName = "extension";
        public const string UrlPropertyName = "url";
        public const string ValueUriPropertyName = "valueUri";
        public const string ValueCodePropertyName = "valueCode";

        public static readonly string[] SmartCapabilityLaunches = new[]
        {
            "launch-standalone",
        };

        public static readonly string[] SmartCapabilityClients = new[]
        {
            "client-public",
            "client-confidential-symmetric",
            "client-confidential-asymmetric",
        };

        public static readonly string[] SmartCapabilityPermissions = new[]
        {
            "permission-patient",
            "permission-user",
            "permission-offline",
            "permission-v2",
        };

        public static readonly string[] SmartCapabilitySSOs = new[]
        {
            "sso-openid-connect",
        };

        public static readonly string[] SmartCapabilityAdditional = new[]
        {
            "authorize-post",
        };

        public static readonly string[] SmartCapabilityThirdPartyContexts = new[]
        {
            "context-ehr-encounter",
        };

        public static ref readonly Coding RestfulSecurityServiceOAuth => ref RestfulSecurityServiceOAuthCodeableConcept;

        public static ref readonly Coding RestfulSecurityServiceStu3OAuth => ref RestfulSecurityServiceStu3OAuthCodeableConcept;
    }
}
