export function startPlayer(stream_url, stream_type) {

    const player = OvenPlayer.create('player_id', {
        "autoStart": true,
        "mute": true,
        "autoReload": true,
        "autoReloadInterval": "2000",
        sources: [
            {
                label: 'label_for_webrtc',
                type: stream_type,
                file: stream_url
            }
        ]
    });
}
