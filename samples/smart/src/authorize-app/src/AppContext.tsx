// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

import React, { useContext, createContext, useState, useEffect } from 'react';
import { IMsalContext, useMsal  } from '@azure/msal-react';
import { getAppConsentInfo, getLoginHint, saveAppConsentInfo } from './GraphConsentService';
import { selectProperties } from '@fluentui/react';
import internal from 'stream';
import { apiEndpoint } from './Config';


// Core application context object.
type AppContext = {
  consentInfo?: AppConsentInfo
  requestedScopesDisplay?: string[]
  user?: AppUser
  error?: AppError
  displayError?: Function
  clearError?: Function
  saveScopes?: ((authInfo: AppConsentInfo) => Promise<void>)
  logout?: Function
}

// Information about the user from Microsoft Graph.
export interface AppUser {
  id: string
  displayName: string
  email: string
};

// Information about the application the user is logging into.
export interface AppConsentInfo {
  applicationId: string
  applicationName: string
  applicationDescription?: string
  applicationUrl?: string
  scopes: AppConsentScope[]
}

// Detailed scope information.
export interface AppConsentScope {
  name: string
  id: string
  userDescription: string
  resourceId: string
  consented: boolean
  consentId?: string
  enabled?: boolean
  hidden?: boolean
}

// Object to hold application errors to display.
export interface AppError {
  message: string
  debug?: string
};

export const queryParams = new URLSearchParams(window.location.search);
const applicationId = queryParams.get("client_id") ?? undefined;
const requestedScopes = queryParams.get("scope") ?? undefined;

// Create starter (null) context.
const appContext = createContext<AppContext>({
  consentInfo: undefined,
  requestedScopesDisplay: undefined,
  user: undefined,
  error: undefined,
  displayError: undefined,
  clearError: undefined,
  saveScopes: undefined
});

export function useAppContext(): AppContext {
  return useContext(appContext);
}

interface ProvideAppContextProps {
  children: React.ReactNode;
}

export default function ProvideAppContext({ children }: ProvideAppContextProps) {
  const auth = useProvideAppContext();
  return (
    <appContext.Provider value={auth}>
      {children}
    </appContext.Provider>
  );
}

function useProvideAppContext() {
  // Fetches user information post login.
  const msal : IMsalContext = useMsal();

  // Raw placeholder for Graph User info.
  const [user, setUser] = useState<AppUser | undefined>(undefined);
  
  // Raw placeholder for app consent information
  const [appConsentInfo, SetAppConsentInfo] = useState<AppConsentInfo | undefined>(undefined);
  
  // App error - used to display errors to the user.
  const [error, setError] = useState<AppError | undefined>(undefined);

  const displayError = (message: string, debug?: string) => {
    setError({ message, debug });
  }

  const clearError = () => {
    setError(undefined);
  }

  const logoutUser = () => {
    msal.instance.logoutRedirect();
  };

  const setUserIfEmpty = async () => {
    if (!user && applicationId && msal?.instance && error == undefined) {

      let account = msal.instance.getActiveAccount();

      if (account) {
        try {
          setUser({
            id: account?.localAccountId || "",
            displayName: account?.name ?? "Guest User",
            email: account?.username ?? ""
          });
        }
        catch (err: any) {
          displayError('Error getting user', error);
        }
      }
    }
  };


const setAppConsentInfoIfEmpty = async () => {
  if (applicationId != undefined && requestedScopes != undefined && appConsentInfo == undefined && error == undefined)
  {
    try {
      const info = await getAppConsentInfo(applicationId, requestedScopes);
      info.scopes.forEach(scope => {
        scope.hidden = shouldScopeBeHiddenAndAlwaysEnabled(scope.name);
        scope.enabled = shouldScopeBeHiddenAndAlwaysEnabled(scope.name) || scope.consented
      });

      SetAppConsentInfo(info);
    }
    catch (err: any) {
      displayError(err.message);
    }
  }
};

const sleep = (ms: number) => new Promise(
  resolve => setTimeout(resolve, ms)
);

const saveScopes = async (modifiedAuthInfo: AppConsentInfo) : Promise<void> => {
  console.log("Saving scopes...");

  // We only care about removing scopes if needed. Consent prompt will ask user to re-add any needed scopes.
  if (modifiedAuthInfo.scopes.filter(x => x.enabled == false && x.consented == true).length >= 0) {
    
    // Convert scopes the user has enabled to consented for the API call.
    modifiedAuthInfo.scopes.forEach(scope => {
      scope.consented = scope.enabled ?? false;
    });

    // Save the consent information.
    try
    {
      await saveAppConsentInfo(modifiedAuthInfo);
    }
    catch (err: any) {
      displayError(err.message);
      return;
    }

    // Get the new consent information, retry until any scope removals are reflected.
    let retryAttempt = 0;
    let scopeSaveSuccessful: boolean = false;
    const userInputConsentedScopeCount = modifiedAuthInfo.scopes.filter(x => x.consented).length;

    while(retryAttempt < 10) {
      try {
        let newInfo = await getAppConsentInfo(modifiedAuthInfo.applicationId, "");
        let currentGraphConsentCount = newInfo.scopes.filter(x => x.consented).length; 
        
        console.log(`Checking if scopes have been removed. Attempt ${retryAttempt} of 10. User consented scope count: ${userInputConsentedScopeCount}. New consented scope count: ${currentGraphConsentCount}.`);

        // We want the current graph consented scopes to be less than what the user needs to consent to. This will enable the Graph consent dialogue. 
        if (userInputConsentedScopeCount >= currentGraphConsentCount) {
          scopeSaveSuccessful = true;
          break;
        }

        retryAttempt++;
        await sleep(1000 * retryAttempt);
      }
      catch (err: any) {
        displayError(err.message);
        return;
      }
    }

    // Give graph more time to replicate the consent information.
    console.log("Sleeping for 30 seconds to ensure graph will have the latest consent information.");
    await sleep(30000);

    if (!scopeSaveSuccessful) {
      displayError("Scopes did not properly replicate.");
      return;
    }
  }

  // Redirect to authorization endpoint.
  const newQueryParams = queryParams;
  newQueryParams.set("scope", modifiedAuthInfo.scopes.filter(x => x.enabled).map(x => x.name).join(" "));
  newQueryParams.set("user", "true");

  const hint = await getLoginHint();
  if (hint.length > 0) {
    newQueryParams.set("login_hint", hint)
  }
  window.location.assign(apiEndpoint + "/authorize?" + newQueryParams.toString());
}

useEffect(() => {
  if (applicationId == undefined || requestedScopes == undefined) {
    if (error == undefined) {
      displayError("Missing required parameters in the URL.", "client_id and scope are required.");
    }
  }
  else {
    if (error == undefined) {
      setUserIfEmpty();
      setAppConsentInfoIfEmpty();
    }
  }
}, []);

const requestedScopesDisplay = requestedScopes?.split(" ").filter(x => !shouldScopeBeHiddenAndAlwaysEnabled(x)) || [];

let authInfo: AppContext = {
  consentInfo: appConsentInfo,
  requestedScopesDisplay: requestedScopesDisplay,
  user: user,
  error: error,
  displayError: displayError,
  clearError: clearError,
  saveScopes: saveScopes,
  logout: logoutUser
};

return authInfo;
}


const shouldScopeBeHiddenAndAlwaysEnabled = (scope: string): boolean => {
  const scopeLower = scope.toLowerCase();
  return scopeLower.includes("launch") || scopeLower == "openid" || scopeLower == "fhiruser";
};