// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

import React, { useContext, createContext, useState, useEffect } from 'react';
import { IMsalContext, useMsal  } from '@azure/msal-react';
import { getAppConsentInfo } from './GraphConsentService';


// Core application context object.
type AppContext = {
  consentInfo?: AppConsentInfo
  requestedScopes?: string[]
  user?: AppUser
  error?: AppError
  displayError?: Function
  clearError?: Function
  saveScopes?: ((authInfo: AppConsentInfo) => Promise<boolean>)
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
  hidden?: boolean
}

// Object to hold application errors to display.
export interface AppError {
  message: string
  debug?: string
};

// Create starter (null) context.
const appContext = createContext<AppContext>({
  consentInfo: undefined,
  requestedScopes: undefined,
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

export const queryParams = new URLSearchParams(window.location.search);

function useProvideAppContext() {
  // Fetches user information post login.
  const msal : IMsalContext = useMsal();

  // Raw placeholder for Graph User info.
  const [user, setUser] = useState<AppUser | undefined>(undefined);
  
  // Raw placeholder for app consent information
  const [appConsentInfo, SetAppConsentInfo] = useState<AppConsentInfo | undefined>(undefined);

  const [requestedScopes, setRequestedScopes] = useState<string[] | undefined>(undefined);
  
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

  const applicationId = queryParams.get("client_id") ?? undefined;
  const scope = queryParams.get("scope") ?? undefined;

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
  if (applicationId != undefined && scope != undefined && appConsentInfo == undefined && error == undefined)
  {
    try {
      const info = await getAppConsentInfo(applicationId, scope);
      SetAppConsentInfo(info);
    }
    catch (err: any) {
      displayError(err.message);
    }
  }
};

const setRequestedScopesIfEmpty = async () => {
  if (scope != undefined && requestedScopes == undefined && error == undefined)
  {
    setRequestedScopes(scope?.split(" ") ?? []);
  }
}

const saveScopes = async (modifiedAuthInfo: AppConsentInfo) : Promise<boolean> => {
  console.log("Saving scopes...");
  return true;
}

useEffect(() => {
  if (applicationId == undefined || scope == undefined) {
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
});

let authInfo: AppContext = {
  consentInfo: appConsentInfo,
  user: user,
  error: error,
  displayError: displayError,
  clearError: clearError,
  saveScopes: saveScopes,
  logout: logoutUser
};

return authInfo;
}


const shouldScopeBeHidden = (scope: string): boolean => {
  const scopeLower = scope.toLowerCase();
  return scopeLower.includes("launch") || scopeLower == "openid" || scopeLower == "fhiruser";
};