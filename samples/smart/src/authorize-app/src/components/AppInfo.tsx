import { useEffect, useState, FC, ReactElement } from 'react';
import { Stack, Text, } from '@fluentui/react';

interface AppInfoProps {
    applicationName: string
    applicationDescription?: string
    applicationUrl?: string
}

export const AppInfo: FC<AppInfoProps> = (props: AppInfoProps): ReactElement => {
    const [displayName, setDisplayName] = useState(props.applicationName);
    const [description, setDescription] = useState(props.applicationDescription);
    const [infoUrl, setInfoUrl] = useState(props.applicationUrl);

    useEffect(() => {
        setDisplayName(props.applicationName|| '');
        setDescription(props.applicationDescription || '');
        setInfoUrl(props.applicationUrl);
    }, [props]);

    return (
        <Stack>
            {props.applicationName &&
                <>

                    <Stack.Item>
                        <Text block variant="xLarge">Application {displayName} is requesting access to your information.</Text>
                        {
                            description &&
                            <Text variant="small">{description}</Text>
                        }
                        {infoUrl &&
                            <Text variant="small">For more information, see {infoUrl}.</Text>
                        }
                    </Stack.Item>
                </>
            }
        </Stack>
    )
}

export default AppInfo;