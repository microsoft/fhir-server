/*
 * Copyright (c) Microsoft Corporation. All rights reserved.
 * Licensed under the MIT License.
 */

import { LogLevel } from "@azure/msal-browser";

/**
 * Configuration object to be passed to MSAL instance on creation. 
 * For a full list of MSAL.js configuration parameters, visit:
 * https://github.com/AzureAD/microsoft-authentication-library-for-js/blob/dev/lib/msal-browser/docs/configuration.md 
 */
export const msalConfig = {
    auth: {
        clientId: window.ENV_CONFIG.REACT_APP_AAD_APP_CLIENT_ID, 
        authority: `https://login.microsoftonline.com/${window.ENV_CONFIG.REACT_APP_AAD_APP_TENANT_ID}`,
        redirectUri: window.location.protocol + "//" + window.location.host,
        postLogoutRedirectUri: "https://www.microsoft.com",
    },
    cache: {
        cacheLocation: "sessionStorage", // This configures where your cache will be stored
        storeAuthStateInCookie: false, // Set this to "true" if you are having issues on IE11 or Edge
    },
    system: {	
        loggerOptions: {	
            loggerCallback: (level: LogLevel, message : string, containsPii : boolean) => {	
                if (containsPii) {		
                    return;		
                }		
                switch (level) {
                    case LogLevel.Error:
                        console.error(message);
                        return;
                    case LogLevel.Info:
                        console.info(message);
                        return;
                    case LogLevel.Verbose:
                        console.debug(message);
                        return;
                    case LogLevel.Warning:
                        console.warn(message);
                        return;
                    default:
                        return;
                }	
            }	
        }	
    }
};

export const scopes: string[] = [`api://${msalConfig.auth.clientId}//user_impersonation`];
export const apiEndpoint: string = window.ENV_CONFIG.REACT_APP_API_BASE_URL || "http://localhost:7081/api";