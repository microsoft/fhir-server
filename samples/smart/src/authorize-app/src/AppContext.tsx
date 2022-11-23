// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

import React, { useContext, createContext, useState, useEffect } from 'react';
import { IMsalContext, useMsal  } from '@azure/msal-react';
import { getAppConsentInfo, saveAppConsentInfo } from './GraphConsentService';


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


const saveScopes = async (modifiedAuthInfo: AppConsentInfo) : Promise<void> => {
  console.log("Saving scopes...");

  if (modifiedAuthInfo.scopes.filter(x => x.enabled != x.consented).length >= 0) {
    
    // Convert scopes the user has enabled to consented for the API call.
    modifiedAuthInfo.scopes.forEach(scope => {
      scope.consented = scope.enabled ?? false;
    });

    try
    {
      await saveAppConsentInfo(modifiedAuthInfo);
    }
    catch (err: any) {
      displayError(err.message);
      return;
    }
  }

  // Redirect to authorization endpoint.
  const newQueryParams = queryParams;
  newQueryParams.set("scope", modifiedAuthInfo.scopes.filter(x => x.enabled).map(x => x.name).join(" "));
  newQueryParams.set("user", "true");
  window.location.assign("https://mikaelw-smart5-apim.azure-api.net/smart/authorize?" + newQueryParams.toString());
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