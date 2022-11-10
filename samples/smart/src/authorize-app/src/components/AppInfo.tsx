import { useEffect, useState, FC, ReactElement } from 'react';
import { Stack, Text, } from '@fluentui/react';
import { Application } from '@microsoft/microsoft-graph-types';

interface AppInfoProps {
    appInfo?: Application
}

export const AppInfo: FC<AppInfoProps> = (props: AppInfoProps): ReactElement => {
    const [displayName, setDisplayName] = useState(props.appInfo?.displayName);
    const [description, setDescription] = useState(props.appInfo?.description);
    const [infoUrl, setInfoUrl] = useState(props.appInfo?.info);

    useEffect(() => {
        setDisplayName(props.appInfo?.displayName || '');
        setDescription(props.appInfo?.description || '');
        setInfoUrl(props.appInfo?.info);
    }, [props]);

    return (
        <Stack>
            {props.appInfo &&
                <>

                    <Stack.Item>
                        <Text block variant="xLarge">Application {displayName} is requesting access to your information.</Text>
                        <Text variant="small">{description}</Text>
                        <Text variant="small">For more information, see {infoUrl?.marketingUrl}.</Text>
                    </Stack.Item>
                </>
            }
        </Stack>
    )
}

export default AppInfo;