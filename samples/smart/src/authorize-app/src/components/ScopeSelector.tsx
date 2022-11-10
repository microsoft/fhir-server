import React, { useEffect, useState, FC, ReactElement } from 'react';
import { Stack, Text, List } from '@fluentui/react';
import { Application, OAuth2PermissionGrant, RequiredResourceAccess } from '@microsoft/microsoft-graph-types';

interface ScopeSelectorProps {
    appScopes?: RequiredResourceAccess[];
    currentlyApprovedScopes?: OAuth2PermissionGrant[];
    requestedScopes?: string[];
}

export const ScopeSelector: FC<ScopeSelectorProps> = ( props: ScopeSelectorProps): ReactElement => {
    const [appScopes, setAppScopes] = useState(props.appScopes);
    const [currentlyApprovedScopes, setCurrentlyApprovedScopes] = useState(props.currentlyApprovedScopes);
    const [requestedScopes, setRequestedScopes] = useState(props.requestedScopes);

    useEffect(() => {
        setAppScopes(props.appScopes);
        setCurrentlyApprovedScopes(props.currentlyApprovedScopes);
        setRequestedScopes(props.requestedScopes);

        if (props.currentlyApprovedScopes)
        {
            let test = currentlyApprovedScopes;
            console.log(test);
        }

    }, [props]);

    return (
        <Stack>
            <Stack.Item>
                <Text block variant="xLarge">Requested Scopes:</Text>
                <List items={requestedScopes?.map((x) => ({name: x}))} />
            </Stack.Item>
            <Stack.Item>
                <Text block variant="xLarge">CurrentScopes Scopes:</Text>
                <List items={currentlyApprovedScopes?.map((x) => ({name: x.scope}))} />
            </Stack.Item>
        </Stack>
    )
}

export default ScopeSelector;