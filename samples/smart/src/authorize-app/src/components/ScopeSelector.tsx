import React, { useEffect, useState, FC, ReactElement } from 'react';
import { Stack, Text, List, IStackStyles, PrimaryButton, DefaultButton, Checkbox, Spinner, SpinnerSize } from '@fluentui/react';
import { Application, OAuth2PermissionGrant, RequiredResourceAccess } from '@microsoft/microsoft-graph-types';

import { AuthorizeRequestInfo, Scopes } from '../AppContext';

const moduleStyle: IStackStyles = {
    root: {
        paddingBottom: 20,
    }
}

interface ScopeSelectorProps {
    authInfo?: AuthorizeRequestInfo
    updateUserApprovedScopes?: (scopes: AuthorizeRequestInfo) => Promise<boolean>
}

export const ScopeSelector: FC<ScopeSelectorProps> = (props: ScopeSelectorProps): ReactElement => {
    const [authInfo, setAuthInfo] = useState(props.authInfo);
    const [updateNeeded, setUpdateNeeded] = useState(false);
    const [mode, setMode] = useState("loading");

    useEffect(() => {
        setAuthInfo(props.authInfo);

        // #TODO - check other state elements (like update function) before changing state from loading

        // Set the initial state value
        if (props.authInfo && mode == "loading")
        {
            if (props.authInfo.applicationScopes.filter(x => x.enabled).length > 0) {
                setMode('existing review');
            }
            else {
                setMode('new edit');
                setUpdateNeeded(true);
            }
        }
    }, [props]);

    const changeEditMode = () => {
        setMode('existing edit');
        setUpdateNeeded(true);
    };

    const handleScopeChecked = (scope: Scopes) => {
        return (ev?: React.FormEvent<HTMLElement | HTMLInputElement>, isChecked?: boolean) => {
            scope.enabled = isChecked!;
            const updateAuthInfo = { 
                applicationId: authInfo!.applicationId,
                enterpriseApplicationId: authInfo!.enterpriseApplicationId,
                requestedScopes: authInfo!.requestedScopes,
                applicationScopes: authInfo!.applicationScopes.map(x => x.name == scope.name && x.resourceId == scope.resourceId ? scope : x),
            };
            setAuthInfo(updateAuthInfo);
            setUpdateNeeded(true);
        }
    }

    const updateScopes = () => {
        if (props.updateUserApprovedScopes) {
            setMode('redirecting');

            if (updateNeeded)
            {
                props.updateUserApprovedScopes(authInfo!);
            }
        }
    };

    return (
        <Stack>
            <Stack.Item align='start'>
                { (mode === 'loading') && <Spinner size={SpinnerSize.large} label="Loading..." ariaLive="assertive" />}
                { (mode === 'redirecting') && <Spinner size={SpinnerSize.large} label="Redirecting..." ariaLive="assertive" />}
            </Stack.Item>

            { (mode.includes('existing') || mode.includes('new')) &&
                <Stack.Item styles={moduleStyle}>
                    <Text block variant="xLarge">Requested Access:</Text>
                    <List items={authInfo?.requestedScopes?.map(x => ({ name: x }))} />
                </Stack.Item>
            }

            { mode.includes('existing') &&
                <Stack.Item styles={moduleStyle}>
                    <Text block variant="xLarge">Approved Access:</Text>
                    <List items={authInfo?.applicationScopes.filter(x => x.alreadyConsented).filter(x => !x.hidden)} />
                </Stack.Item>
            }

            { mode.includes('edit') &&
                <Stack.Item styles={moduleStyle}>
                    <Text block variant="xLarge">Select Access:</Text>
                    {authInfo?.applicationScopes.map((scope) => (
                        scope.hidden ? null : <Checkbox key={scope.id} label={scope.name} checked={scope.enabled} onChange={handleScopeChecked(scope)} />
                    ))}
                </Stack.Item>
            }

            { mode != 'loading' && mode != 'redirecting' &&
                <Stack.Item styles={moduleStyle}>
                    <Stack horizontal>
                        <PrimaryButton text="Continue" onClick={updateScopes} />

                        { mode === 'existing review' && <DefaultButton text="Change Access" onClick={changeEditMode} />}
                    </Stack>
                </Stack.Item>
            }
        </Stack>
    )
}

export default ScopeSelector;