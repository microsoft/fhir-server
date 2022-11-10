// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

import React, { useContext, createContext, useState, useEffect } from 'react';
import { useMsal } from '@azure/msal-react';
import { Application, ServicePrincipal, OAuth2PermissionGrant } from '@microsoft/microsoft-graph-types';

import { getApplication, getServicePrincipal, getAppCurrentScopes, getUser } from './GraphService';

export interface AppUser {
    id?: string
    displayName?: string,
    email?: string,
    avatar?: string,
    timeZone?: string,
    timeFormat?: string
  };

  export interface AuthorizeRequestInfo {
    client_id?: string,
    scope?: string[]
  }
  
  export interface AppError {
    message: string,
    debug?: string
  };
  
  type AppContext = {
    authInfo?: AuthorizeRequestInfo
    user?: AppUser;
    application?: Application
    requestedResourcePrincipal?: ServicePrincipal,
    currentlyApprovedScopes?: OAuth2PermissionGrant[],
    error?: AppError;
    displayError?: Function;
    clearError?: Function;
  }
  
  const appContext = createContext<AppContext>({
    authInfo: undefined,
    user: undefined,
    application: undefined,
    requestedResourcePrincipal: undefined,
    currentlyApprovedScopes: undefined,
    error: undefined,
    displayError: undefined,
    clearError: undefined
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
    const msal = useMsal();
    const [user, setUser] = useState<AppUser | undefined>(undefined);
    const [application, SetApplication] = useState<Application | undefined>(undefined);
    const [resourcePrincipal, SetResourcePrincipal] = useState<ServicePrincipal | undefined>(undefined);
    const [currentScopes, SetCurrentScopes] = useState<OAuth2PermissionGrant[] | undefined>(undefined);
    const [error, setError] = useState<AppError | undefined>(undefined);
  
    let authInfo : AuthorizeRequestInfo = {
      client_id: queryParams.get("client_id") ?? undefined,
      scope: queryParams.get("scope")?.split(" ") ?? undefined
    }

    const displayError = (message: string, debug?: string) => {
      setError({ message, debug });
    }
  
    const clearError = () => {
      setError(undefined);
    }

    useEffect(() => {     
      const setUserIfEmpty = async () => {
        if (!user && authInfo.client_id) {
          try {
            // Check if user is already signed in
            const account = msal.instance.getActiveAccount();
            if (account) {
              // Get the user from Microsoft Graph
              const user = await getUser();
  
              setUser({
                id: user.id || '',
                displayName: user.displayName  || '',
                email: user.mail || user.userPrincipalName || '',
                timeFormat: user.mailboxSettings?.timeFormat || 'h:mm a',
                timeZone: user.mailboxSettings?.timeZone || 'UTC'
              });
            }
          } catch (err: any) {
            displayError(err.message);
          }
        }
      };

      const setClientApplicationIfEmpty = async () => {
        if (!application && user && authInfo.client_id) {
          try {
            const application = await getApplication(authInfo.client_id);

            if (application)
            {
              application.info = {
                ...application.info,
                marketingUrl: application.info?.marketingUrl ?? "http://testing.com",
              }
            }

            SetApplication(application);
          } catch (err: any) {
            displayError(err.message);
          }
        }
      };

      const setResourcePrincipalIfEmpty = async () => {
        if (!resourcePrincipal && user && authInfo.client_id) {
          try {
            const resourcePrincipal = await getServicePrincipal(authInfo.client_id);
            SetResourcePrincipal(resourcePrincipal);
          } catch (err: any) {
            displayError(err.message);
          }
        }
      };

      const setCurrentScopesIfEmpty = async () => {
        if (!currentScopes && user?.id && resourcePrincipal?.id) {
          try {
            const currentScopes = await getAppCurrentScopes(resourcePrincipal.id, user.id);
            SetCurrentScopes(currentScopes);
          } catch (err: any) {
            displayError(err.message);
          }
        }
      };

      setUserIfEmpty();
      setClientApplicationIfEmpty();
      setResourcePrincipalIfEmpty();
      setCurrentScopesIfEmpty();
    });
  
    return {
      authInfo,
      user,
      application,
      resourcePrincipal,
      currentScopes,
      error,
      displayError,
      clearError
    };
  }