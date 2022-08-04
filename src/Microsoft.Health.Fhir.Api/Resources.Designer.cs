﻿//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//     Runtime Version:4.0.30319.42000
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Api {
    using System;
    
    
    /// <summary>
    ///   A strongly-typed resource class, for looking up localized strings, etc.
    /// </summary>
    // This class was auto-generated by the StronglyTypedResourceBuilder
    // class via a tool like ResGen or Visual Studio.
    // To add or remove a member, edit your .ResX file then rerun ResGen
    // with the /str option, or rebuild your VS project.
    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("System.Resources.Tools.StronglyTypedResourceBuilder", "17.0.0.0")]
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
    [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute()]
    public class Resources {
        
        private static global::System.Resources.ResourceManager resourceMan;
        
        private static global::System.Globalization.CultureInfo resourceCulture;
        
        [global::System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        internal Resources() {
        }
        
        /// <summary>
        ///   Returns the cached ResourceManager instance used by this class.
        /// </summary>
        [global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Advanced)]
        public static global::System.Resources.ResourceManager ResourceManager {
            get {
                if (object.ReferenceEquals(resourceMan, null)) {
                    global::System.Resources.ResourceManager temp = new global::System.Resources.ResourceManager("Microsoft.Health.Fhir.Api.Resources", typeof(Resources).Assembly);
                    resourceMan = temp;
                }
                return resourceMan;
            }
        }
        
        /// <summary>
        ///   Overrides the current thread's CurrentUICulture property for all
        ///   resource lookups using this strongly typed resource class.
        /// </summary>
        [global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Advanced)]
        public static global::System.Globalization.CultureInfo Culture {
            get {
                return resourceCulture;
            }
            set {
                resourceCulture = value;
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to The configuration collection reference &apos;{0}&apos; is not configured..
        /// </summary>
        public static string AnonymizationConfigCollectionNotConfigured {
            get {
                return ResourceManager.GetString("AnonymizationConfigCollectionNotConfigured", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to The parameters &apos;_anonymizationConfigEtag&apos; and &apos;_anonymizationConfigCollectionReference&apos; cannot be given in the same request. Please only specify one of them..
        /// </summary>
        public static string AnonymizationParameterConflict {
            get {
                return ResourceManager.GetString("AnonymizationParameterConflict", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Back to top.
        /// </summary>
        public static string BackToTop {
            get {
                return ResourceManager.GetString("BackToTop", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to The number of entries in the bundle exceeded the configured limit of {0}..
        /// </summary>
        public static string BundleEntryLimitExceeded {
            get {
                return ResourceManager.GetString("BundleEntryLimitExceeded", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to The route for &quot;{0}&quot; was not found..
        /// </summary>
        public static string BundleNotFound {
            get {
                return ResourceManager.GetString("BundleNotFound", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Operation was not attempted because search criteria was not selective enough for query parameter &apos;{0}&apos;..
        /// </summary>
        public static string ConditionalOperationInBundleNotSelectiveEnough {
            get {
                return ResourceManager.GetString("ConditionalOperationInBundleNotSelectiveEnough", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Missing &apos;_anonymizationConfig&apos; for anonymized export.
        /// </summary>
        public static string ConfigLocationRequiredForAnonymizedExport {
            get {
                return ResourceManager.GetString("ConfigLocationRequiredForAnonymizedExport", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Missing &apos;_container&apos; for anonymized export.
        /// </summary>
        public static string ContainerIsRequiredForAnonymizedExport {
            get {
                return ResourceManager.GetString("ContainerIsRequiredForAnonymizedExport", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to The &quot;content-type&quot; header must be &apos;application/x-www-form-urlencoded&apos;..
        /// </summary>
        public static string ContentTypeFormUrlEncodedExpected {
            get {
                return ResourceManager.GetString("ContentTypeFormUrlEncodedExpected", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to The &quot;content-type&quot; header is required..
        /// </summary>
        public static string ContentTypeHeaderRequired {
            get {
                return ResourceManager.GetString("ContentTypeHeaderRequired", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Convert data does not support the following parameter {0} for a POST operation..
        /// </summary>
        public static string ConvertDataParameterNotValid {
            get {
                return ResourceManager.GetString("ConvertDataParameterNotValid", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Convert data operation parameters must be specified as a FHIR Parameters resource.  The body provided in this request is not valid..
        /// </summary>
        public static string ConvertDataParametersNotValid {
            get {
                return ResourceManager.GetString("ConvertDataParametersNotValid", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Value of the following parameter {0} is invalid..
        /// </summary>
        public static string ConvertDataParameterValueNotValid {
            get {
                return ResourceManager.GetString("ConvertDataParameterValueNotValid", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to The template collection reference &apos;{0}&apos; is not configured..
        /// </summary>
        public static string ConvertDataTemplateNotConfigured {
            get {
                return ResourceManager.GetString("ConvertDataTemplateNotConfigured", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to The maximum length of a custom audit header value is {0}. The supplied custom audit header &apos;{1}&apos; has length of {2}..
        /// </summary>
        public static string CustomAuditHeaderTooLarge {
            get {
                return ResourceManager.GetString("CustomAuditHeaderTooLarge", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Must provide ValueSet id or ValueSet Url, but not both..
        /// </summary>
        public static string ExpandInvalidIdParamterXORUrl {
            get {
                return ResourceManager.GetString("ExpandInvalidIdParamterXORUrl", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Must provide ValueSet Parameter Component..
        /// </summary>
        public static string ExpandMissingValueSetParameterComponent {
            get {
                return ResourceManager.GetString("ExpandMissingValueSetParameterComponent", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to If ValueSet ID is not provided, ValueSet URL must be specified. .
        /// </summary>
        public static string ExpandMissingValueSetURL {
            get {
                return ResourceManager.GetString("ExpandMissingValueSetURL", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Failed while running health check..
        /// </summary>
        public static string FailedHealthCheckMessage {
            get {
                return ResourceManager.GetString("FailedHealthCheckMessage", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Failed to create External Terminology Service.
        /// </summary>
        public static string FailedToCreateExternalTerminologyService {
            get {
                return ResourceManager.GetString("FailedToCreateExternalTerminologyService", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Failed to create Fallback Terminology Service.
        /// </summary>
        public static string FailedToCreateFallbackTerminologyService {
            get {
                return ResourceManager.GetString("FailedToCreateFallbackTerminologyService", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Failed to create Validator Resolver.
        /// </summary>
        public static string FailedToCreateValidatorResolver {
            get {
                return ResourceManager.GetString("FailedToCreateValidatorResolver", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to FHIR definitions folder could not be found. Please check that the path is correct..
        /// </summary>
        public static string FHIRDefinitionNotFound {
            get {
                return ResourceManager.GetString("FHIRDefinitionNotFound", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Authorization failed..
        /// </summary>
        public static string Forbidden {
            get {
                return ResourceManager.GetString("Forbidden", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to There was an error processing your request..
        /// </summary>
        public static string GeneralInternalError {
            get {
                return ResourceManager.GetString("GeneralInternalError", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Transaction was failed while processing the request..
        /// </summary>
        public static string GeneralTransactionFailedError {
            get {
                return ResourceManager.GetString("GeneralTransactionFailedError", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Import request must be specified as a Paramters.  The body provided in this request is not valid. .
        /// </summary>
        public static string ImportRequestNotValid {
            get {
                return ResourceManager.GetString("ImportRequestNotValid", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Value of the following parameter {0} is invalid..
        /// </summary>
        public static string ImportRequestValueNotValid {
            get {
                return ResourceManager.GetString("ImportRequestValueNotValid", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Initial import mode is not enabled. Please update service configuration to enable initial import mode..
        /// </summary>
        public static string InitialImportModeNotEnabled {
            get {
                return ResourceManager.GetString("InitialImportModeNotEnabled", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to The input data type &apos;{0}&apos; and default template collection &apos;{1}&apos; are inconsistent..
        /// </summary>
        public static string InputDataTypeAndDefaultTemplateCollectionInconsistent {
            get {
                return ResourceManager.GetString("InputDataTypeAndDefaultTemplateCollectionInconsistent", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to The configuration collection reference &apos;{0}&apos; is invalid..
        /// </summary>
        public static string InvalidAnonymizationConfigCollectionReference {
            get {
                return ResourceManager.GetString("InvalidAnonymizationConfigCollectionReference", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Requested operation &apos;{0}&apos; is not supported using {1}..
        /// </summary>
        public static string InvalidBundleEntry {
            get {
                return ResourceManager.GetString("InvalidBundleEntry", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Bundle.entry.request.url is required..
        /// </summary>
        public static string InvalidBundleEntryRequestUrl {
            get {
                return ResourceManager.GetString("InvalidBundleEntryRequestUrl", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Bundles of type &apos;{0}&apos; are not supported for this operation..
        /// </summary>
        public static string InvalidBundleType {
            get {
                return ResourceManager.GetString("InvalidBundleType", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Invalid compound authorization code..
        /// </summary>
        public static string InvalidCompoundCode {
            get {
                return ResourceManager.GetString("InvalidCompoundCode", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Given conditional reference &apos;{0}&apos; does not resolve to a resource..
        /// </summary>
        public static string InvalidConditionalReference {
            get {
                return ResourceManager.GetString("InvalidConditionalReference", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Resource type and query parameter must be present in a given request &apos;{0}&apos;..
        /// </summary>
        public static string InvalidConditionalReferenceParameters {
            get {
                return ResourceManager.GetString("InvalidConditionalReferenceParameters", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to The requested &quot;_elements&quot; parameter &apos;{0}&apos; is invalid..
        /// </summary>
        public static string InvalidElementsParameter {
            get {
                return ResourceManager.GetString("InvalidElementsParameter", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Invalid launch context parameters..
        /// </summary>
        public static string InvalidLaunchContext {
            get {
                return ResourceManager.GetString("InvalidLaunchContext", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to The output format &apos;{0}&apos; is not supported..
        /// </summary>
        public static string InvalidOutputFormat {
            get {
                return ResourceManager.GetString("InvalidOutputFormat", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to The requested &quot;_pretty&quot; parameter is invalid..
        /// </summary>
        public static string InvalidPrettyParameter {
            get {
                return ResourceManager.GetString("InvalidPrettyParameter", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Provided value for redirect Uri: {0} is not a valid Uri..
        /// </summary>
        public static string InvalidRedirectUri {
            get {
                return ResourceManager.GetString("InvalidRedirectUri", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to The requested &quot;_summary&quot; parameter &apos;{0}&apos; is invalid. Allowed values are &apos;{1}&apos;..
        /// </summary>
        public static string InvalidSummaryParameter {
            get {
                return ResourceManager.GetString("InvalidSummaryParameter", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to The template collection reference &apos;{0}&apos; is invalid..
        /// </summary>
        public static string InvalidTemplateCollectionReference {
            get {
                return ResourceManager.GetString("InvalidTemplateCollectionReference", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Service is locked for initial import mode..
        /// </summary>
        public static string LockedForInitialImportMode {
            get {
                return ResourceManager.GetString("LockedForInitialImportMode", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Must provide System and Code.
        /// </summary>
        public static string LookupInvalidMissingSystemOrCode {
            get {
                return ResourceManager.GetString("LookupInvalidMissingSystemOrCode", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to $member-match operation parameters must be specified as a FHIR Parameters resource.  Provided body in this request is not valid..
        /// </summary>
        public static string MemberMatchInvalidParameter {
            get {
                return ResourceManager.GetString("MemberMatchInvalidParameter", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to &apos;MemberPatient&apos; can&apos;t be found in submitted parameters..
        /// </summary>
        public static string MemberMatchMemberPatientNotFound {
            get {
                return ResourceManager.GetString("MemberMatchMemberPatientNotFound", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to &apos;OldCoverage&apos; can&apos;t be found in submitted parameters. .
        /// </summary>
        public static string MemberMatchOldCoverageNotFound {
            get {
                return ResourceManager.GetString("MemberMatchOldCoverageNotFound", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to API.
        /// </summary>
        public static string MenuAPI {
            get {
                return ResourceManager.GetString("MenuAPI", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Capability Statement.
        /// </summary>
        public static string MenuCapabilityStatement {
            get {
                return ResourceManager.GetString("MenuCapabilityStatement", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to The audit information is missing for Controller: {0} and Action: {1}. This usually means the action is not marked with appropriate attribute..
        /// </summary>
        public static string MissingAuditInformation {
            get {
                return ResourceManager.GetString("MissingAuditInformation", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Only one profile can be provided between a Parameters resource and the URL.
        /// </summary>
        public static string MultipleProfilesProvided {
            get {
                return ResourceManager.GetString("MultipleProfilesProvided", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to The requested route was not found..
        /// </summary>
        public static string NotFoundException {
            get {
                return ResourceManager.GetString("NotFoundException", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Only initial load import is supported. Please add &quot;InitialLoad&quot; mode to parameters..
        /// </summary>
        public static string OnlyInitialImportOperationSupported {
            get {
                return ResourceManager.GetString("OnlyInitialImportOperationSupported", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to {0} operation failed for reason: {1}.
        /// </summary>
        public static string OperationFailed {
            get {
                return ResourceManager.GetString("OperationFailed", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to The requested &quot;{0}&quot; operation is not enabled.
        /// </summary>
        public static string OperationNotEnabled {
            get {
                return ResourceManager.GetString("OperationNotEnabled", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to The requested &quot;{0}&quot; operation is not supported..
        /// </summary>
        public static string OperationNotImplemented {
            get {
                return ResourceManager.GetString("OperationNotImplemented", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to FHIR Server.
        /// </summary>
        public static string PageTitle {
            get {
                return ResourceManager.GetString("PageTitle", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Must provide coding parameter componenet.
        /// </summary>
        public static string ParameterMissingCoding {
            get {
                return ResourceManager.GetString("ParameterMissingCoding", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Error occurred when parsing model: &apos;{0}&apos;..
        /// </summary>
        public static string ParsingError {
            get {
                return ResourceManager.GetString("ParsingError", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to PATCH is not currently supported..
        /// </summary>
        public static string PatchNotSupported {
            get {
                return ResourceManager.GetString("PatchNotSupported", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Provided value for `profile` parameter `{0}` is invalid .
        /// </summary>
        public static string ProfileIsInvalid {
            get {
                return ResourceManager.GetString("ProfileIsInvalid", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Reindex job does not support the following parameter {0} for a {1} operation..
        /// </summary>
        public static string ReindexParameterNotValid {
            get {
                return ResourceManager.GetString("ReindexParameterNotValid", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Reindex operation parameters must be specified as a FHIR Parameters resource.  Provided body in this request is not valid..
        /// </summary>
        public static string ReindexParametersNotValid {
            get {
                return ResourceManager.GetString("ReindexParametersNotValid", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to The security configuration requires the authority to be set to an https address..
        /// </summary>
        public static string RequireHttpsMetadataError {
            get {
                return ResourceManager.GetString("RequireHttpsMetadataError", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Both id and the resource are required..
        /// </summary>
        public static string ResourceAndIdRequired {
            get {
                return ResourceManager.GetString("ResourceAndIdRequired", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Id must be specified in the resource..
        /// </summary>
        public static string ResourceIdRequired {
            get {
                return ResourceManager.GetString("ResourceIdRequired", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Resource type &apos;{0}&apos; in the reference &apos;{1}&apos; is not supported..
        /// </summary>
        public static string ResourceNotSupported {
            get {
                return ResourceManager.GetString("ResourceNotSupported", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Bundle contains multiple entries that refers to the same resource &apos;{0}&apos;..
        /// </summary>
        public static string ResourcesMustBeUnique {
            get {
                return ResourceManager.GetString("ResourcesMustBeUnique", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Resource type in the URL must match resourceType in the resource..
        /// </summary>
        public static string ResourceTypeMismatch {
            get {
                return ResourceManager.GetString("ResourceTypeMismatch", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Toggle navigation.
        /// </summary>
        public static string ToggleNavigation {
            get {
                return ResourceManager.GetString("ToggleNavigation", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to The maximum number of concurrent API calls on this instance was reached..
        /// </summary>
        public static string TooManyConcurrentRequests {
            get {
                return ResourceManager.GetString("TooManyConcurrentRequests", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to The maximum number of custom audit headers allowed is {0}. The number of custom audit headers supplied is {1}..
        /// </summary>
        public static string TooManyCustomAuditHeaders {
            get {
                return ResourceManager.GetString("TooManyCustomAuditHeaders", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Transaction failed on &apos;{0}&apos; for the requested url &apos;{1}&apos;..
        /// </summary>
        public static string TransactionFailed {
            get {
                return ResourceManager.GetString("TransactionFailed", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to The _type parameter must be included when using the _typeFilter parameter. .
        /// </summary>
        public static string TypeFilterWithoutTypeIsUnsupported {
            get {
                return ResourceManager.GetString("TypeFilterWithoutTypeIsUnsupported", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Unable to obtain OpenID configuration..
        /// </summary>
        public static string UnableToObtainOpenIdConfiguration {
            get {
                return ResourceManager.GetString("UnableToObtainOpenIdConfiguration", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Authentication failed..
        /// </summary>
        public static string Unauthorized {
            get {
                return ResourceManager.GetString("Unauthorized", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to The requested &quot;_format&quot; parameter is not supported..
        /// </summary>
        public static string UnsupportedFormatParameter {
            get {
                return ResourceManager.GetString("UnsupportedFormatParameter", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Value supplied for the &quot;{0}&quot; header is not supported..
        /// </summary>
        public static string UnsupportedHeaderValue {
            get {
                return ResourceManager.GetString("UnsupportedHeaderValue", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to The &quot;{0}&quot; parameter is not supported..
        /// </summary>
        public static string UnsupportedParameter {
            get {
                return ResourceManager.GetString("UnsupportedParameter", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Requested operation does not support resource type: {0}..
        /// </summary>
        public static string UnsupportedResourceType {
            get {
                return ResourceManager.GetString("UnsupportedResourceType", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Id in the URL must match id in the resource..
        /// </summary>
        public static string UrlResourceIdMismatch {
            get {
                return ResourceManager.GetString("UrlResourceIdMismatch", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Parameter must provide a &quot;coding&quot; and ValueSet/CodeSystem parameter components..
        /// </summary>
        public static string ValidateCodeInvalidParemeters {
            get {
                return ResourceManager.GetString("ValidateCodeInvalidParemeters", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to $validate-code can only be called on a CodeSystem or ValueSet..
        /// </summary>
        public static string ValidateCodeInvalidResourceType {
            get {
                return ResourceManager.GetString("ValidateCodeInvalidResourceType", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Must provide System and Code..
        /// </summary>
        public static string ValidateCodeMissingSystemOrCode {
            get {
                return ResourceManager.GetString("ValidateCodeMissingSystemOrCode", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Value cannot be null.  Parameter name: {0}..
        /// </summary>
        public static string ValueCannotBeNull {
            get {
                return ResourceManager.GetString("ValueCannotBeNull", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to View was not found..
        /// </summary>
        public static string ViewNotFound {
            get {
                return ResourceManager.GetString("ViewNotFound", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to STU3 API &lt;small&gt;preview&lt;/small&gt;.
        /// </summary>
        public static string WelcomeTitle {
            get {
                return ResourceManager.GetString("WelcomeTitle", resourceCulture);
            }
        }
    }
}
