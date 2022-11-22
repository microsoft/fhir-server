// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

import React, { useContext, createContext, useState, useEffect } from 'react';
import { IMsalContext, useMsal  } from '@azure/msal-react';
import { Application, ServicePrincipal, OAuth2PermissionGrant } from '@microsoft/microsoft-graph-types';

import { getApplication, getServicePrincipal, getAppCurrentScopes, getUser, patchAppCurrentScopes, createAppCurrentScopes } from './GraphService';
import { msalInstance } from './App';


// Core application context object.
type AppContext = {
  authInfo?: AuthorizeRequestInfo
  appInfo?: Application,
  user?: AppUser
  error?: AppError
  displayError?: Function
  clearError?: Function
  saveScopes?: ((authInfo: AuthorizeRequestInfo) => Promise<boolean>)
  logout?: Function
}

// Information about the user from Microsoft Graph.
export interface AppUser {
  id: string
  displayName: string
  email: string
};

// Information about the application the user is logging into.
export interface AuthorizeRequestInfo {
  applicationId: string
  enterpriseApplicationId: string
  requestedScopes: string[]
  applicationScopes: Scopes[]
}

// Detailed scope information.
export interface Scopes {
  name: string
  id: string
  userDescription: string
  resourceId: string
  alreadyConsented: boolean
  consentId?: string
  enabled: boolean
  hidden: boolean
}

// Object to hold application errors to display.
export interface AppError {
  message: string
  debug?: string
};

// Create starter (null) context.
const appContext = createContext<AppContext>({
  authInfo: undefined,
  appInfo: undefined,
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
  // Raw placeholder for Graph Requesting Application (global application) info.
  const [application, SetApplication] = useState<Application | undefined>(undefined);
  // Raw placeholder for Graph Requesting Enterprise Application (tenant specific) info.
  const [appServicePrincipal, SetAppServicePrincipal] = useState<ServicePrincipal | undefined>(undefined);
  // Raw placeholder for Graph current user consented scopes.
  const [currentScopes, SetCurrentScopes] = useState<OAuth2PermissionGrant[] | undefined>(undefined);
  // Raw placeholder for Graph resource service principals.
  const [resourceServicePrincipals, SetResourceServicePrincipals] = useState<ServicePrincipal[]>([]);

  // Holds mapped authorization information.
  const [authorizeInfo, SetAuthorizeInfo] = useState<AuthorizeRequestInfo | undefined>(undefined);
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
  const requestedScopes = queryParams.get("scope")?.split(" ") ?? undefined;

  const setUserIfEmpty = async () => {
    if (!user && applicationId && msal?.instance && error == undefined) {

      let account = msal.instance.getActiveAccount();

      if (account) {
        try {
          // Get the user from Microsoft Graph
          const user = await getUser();
          setUser({
            id: user.id || '',
            displayName: user.displayName || '',
            email: user.mail || user.userPrincipalName || ''
          });
        }
        catch (err: any) {
          displayError('Error getting user', error);
        }
      }
    }
  };

const setClientApplicationIfEmpty = async () => {
  if (application == undefined && user && applicationId && error == undefined) {

    try {
      const application = await getApplication(applicationId);
      if (application) {
        application.info = {
          ...application.info,
          marketingUrl: application.info?.marketingUrl ?? "http://testing.com",
        }
      }

      SetApplication(application);

    }
    catch (err: any) {
      displayError(err.message);
    }
  }
};

const setResourcePrincipalsIfEmpty = async () => {
  if (application && resourceServicePrincipals.length == 0 && error == undefined) {

    const matchedServicePrincipals : ServicePrincipal[] = [];

    for (const resource of application.requiredResourceAccess || [])
    {
      try {
        const servicePrincipal = await getServicePrincipal(resource.resourceAppId!)
        if (servicePrincipal) {
          matchedServicePrincipals.push(servicePrincipal);
        }
        else {
          displayError(`Unable to find service principal for resource ${resource.resourceAppId}`);
        }
      }
      catch (err: any) {
        displayError(err.message, "While fetching resource service principals");
      }
    }

    if (matchedServicePrincipals.length > 0) {
      SetResourceServicePrincipals(matchedServicePrincipals);
    }
  }
};

const SetAppServicePrincipalIfEmpty = async () => {
  if (appServicePrincipal == undefined && user && applicationId && error == undefined) {
    try {
      const servicePrincipal = await getServicePrincipal(applicationId);
      SetAppServicePrincipal(servicePrincipal);

    }
    catch (err: any) {
      displayError(err.message);
    }
  }
};

const setCurrentScopesIfEmpty = async () => {
  if (!currentScopes && user?.id && appServicePrincipal?.id && error == undefined) {
    try {
      const currentScopes = await getAppCurrentScopes(appServicePrincipal.id, user.id);
      SetCurrentScopes(currentScopes);
    }
    catch (err: any) {
      displayError(err.message);
    }
  }
}

const setAuthorizeInfoIfEmpty = async () => {

  try
  {
    if (!authorizeInfo && application && appServicePrincipal && currentScopes && resourceServicePrincipals && error == undefined) {
      const authInfo = mapAuthorizeRequestInfo(application, appServicePrincipal, resourceServicePrincipals, requestedScopes, currentScopes)
      SetAuthorizeInfo(authInfo);
    }
  }
  catch (err: any)
  {
    displayError(`Issue mapping Graph domain to application domain. ${err.message}`);
  }
  

};

const saveScopes = async (modifiedAuthInfo: AuthorizeRequestInfo) : Promise<boolean> => {
  console.log("Saving scopes...");

  const resources = new Set(modifiedAuthInfo.applicationScopes.map(x => x.resourceId));

  for (const resource of resources) {
    const scopesForResource = modifiedAuthInfo.applicationScopes.filter( x=> x.resourceId == resource);

    if (scopesForResource.length > 0) {

      const firstScopeConsentId = scopesForResource.find(x => x.consentId != undefined)?.consentId ?? undefined;

      if (firstScopeConsentId != undefined) {

        // All scopes for this resource have the same consent id, so we can just update the scopes.
        try {
          await patchAppCurrentScopes(firstScopeConsentId, scopesForResource.filter(x => x.enabled).map(x => x.name).join(" "));
        }
        catch (err: any) {
          displayError(`Error updating scope grant. ${err.message}`);
          return false;
        }
      } else {
        // No consent id, so we need to create a new scope grant.
        try {
          await createAppCurrentScopes(modifiedAuthInfo.enterpriseApplicationId, user!.id, resource, scopesForResource.filter(x => x.enabled).map(x => x.name).join(" "));
        }
        catch (err: any) {
          displayError(`Error creating scope grant. ${err.message}`);
          return false;
        }
      } 
    }
  }

  return true;
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
      setClientApplicationIfEmpty();
      setResourcePrincipalsIfEmpty();
      SetAppServicePrincipalIfEmpty();
      setCurrentScopesIfEmpty();
      setAuthorizeInfoIfEmpty();
    }
  }
});

let authInfo: AppContext = {
  authInfo: authorizeInfo,
  appInfo: application,
  user: user,
  error: error,
  displayError: displayError,
  clearError: clearError,
  saveScopes: saveScopes,
  logout: logoutUser
};

return authInfo;
}

const mapAuthorizeRequestInfo = (requestingApp?: Application, requestingEnterpriseApp?: ServicePrincipal, resourceServicePrincipals?: ServicePrincipal[], requestedScopes?: string[], currentScopes?: OAuth2PermissionGrant[]): AuthorizeRequestInfo | undefined => {

  if (!requestingApp || !requestingEnterpriseApp || !resourceServicePrincipals || resourceServicePrincipals.length === 0 || !requestedScopes || !currentScopes) {
    return undefined;
  }

  let applicationScopes: Scopes[] = [];

  for (const scope of requestedScopes) {
    const transformedScope = transformScopeForAad(scope);
    const appAllowedScopeIds = aadAppAllowedScopeIds(requestingApp);

    // Find a matching resource principal for the scope. The resource principal must contain the scope and be enabled on the requesting application.
    const resourceServicePrincipal = resourceServicePrincipals.find(sp => sp.oauth2PermissionScopes?.find(scope => appAllowedScopeIds.includes(scope.id ?? "") && scope.value?.toLowerCase() === transformedScope.toLowerCase()));
    const scopeInfo = resourceServicePrincipal?.oauth2PermissionScopes?.find(x => x.value?.toLowerCase() === transformedScope.toLowerCase());
    const scopeConsentRecord = currentScopes?.find(x => x.resourceId === resourceServicePrincipal?.id && x.scope?.toLowerCase().includes(transformedScope.toLowerCase()));

    if (resourceServicePrincipal?.id && scopeInfo?.id && scopeInfo?.value) {
      applicationScopes.push({
        name: scopeInfo.value,
        id: scopeInfo.id,
        resourceId: resourceServicePrincipal.id,
        alreadyConsented: scopeConsentRecord != undefined,
        consentId: scopeConsentRecord?.id,
        enabled: scopeConsentRecord != undefined,
        userDescription: scopeInfo.userConsentDescription ?? '',
        hidden: shouldScopeBeHidden(scopeInfo.value)
      })
    }
  }

  return {
    applicationId: requestingApp.id || '',
    enterpriseApplicationId: requestingEnterpriseApp.id || '',
    requestedScopes: requestedScopes.filter(x => !shouldScopeBeHidden(x)),
    applicationScopes: applicationScopes,
  };
}

const aadAppAllowedScopeIds = (app: Application) => {
  const ids: string[] = [];

  for (const resource of app.requiredResourceAccess || []) {
    for (const scope of resource.resourceAccess || []) {
      if (scope.type === "Scope" && scope.id) {
        ids.push(scope.id);
      }
    }
  }
  
  return ids;
};


const transformScopeForAad = (scope: string): string => {
  return scope.replace("/", ".").replace("*", "all");
}

const shouldScopeBeHidden = (scope: string): boolean => {
  const scopeLower = scope.toLowerCase();
  return scopeLower.includes("launch") || scopeLower == "openid" || scopeLower == "fhiruser";
};