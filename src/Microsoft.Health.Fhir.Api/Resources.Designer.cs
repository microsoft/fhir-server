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
    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("System.Resources.Tools.StronglyTypedResourceBuilder", "15.0.0.0")]
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
        ///   Looks up a localized string similar to The &quot;content-type&quot; header is required..
        /// </summary>
        public static string ContentTypeHeaderRequired {
            get {
                return ResourceManager.GetString("ContentTypeHeaderRequired", resourceCulture);
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
        ///   Looks up a localized string similar to Failed while running health check..
        /// </summary>
        public static string FailedHealthCheckMessage {
            get {
                return ResourceManager.GetString("FailedHealthCheckMessage", resourceCulture);
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
        ///   Looks up a localized string similar to Requested operation &apos;{0}&apos; is not supported using {1}..
        /// </summary>
        public static string InvalidBundleEntry {
            get {
                return ResourceManager.GetString("InvalidBundleEntry", resourceCulture);
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
        ///   Looks up a localized string similar to Invalid launch context parameters..
        /// </summary>
        public static string InvalidLaunchContext {
            get {
                return ResourceManager.GetString("InvalidLaunchContext", resourceCulture);
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
        ///   Looks up a localized string similar to The requested route was not found..
        /// </summary>
        public static string NotFoundException {
            get {
                return ResourceManager.GetString("NotFoundException", resourceCulture);
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
        ///   Looks up a localized string similar to The requested &quot;{0}&quot; operation is not implemented..
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
        ///   Looks up a localized string similar to PATCH is not currently supported..
        /// </summary>
        public static string PatchNotSupported {
            get {
                return ResourceManager.GetString("PatchNotSupported", resourceCulture);
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
        ///   Looks up a localized string similar to The requested &quot;{0}&quot; operation is not supported..
        /// </summary>
        public static string UnsupportedOperation {
            get {
                return ResourceManager.GetString("UnsupportedOperation", resourceCulture);
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
        ///   Looks up a localized string similar to The supplied value for &quot;{0}&quot; paramter is invalid..
        /// </summary>
        public static string UnsupportedParameterValue {
            get {
                return ResourceManager.GetString("UnsupportedParameterValue", resourceCulture);
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
