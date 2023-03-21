export function startPlayer(stream_url) {

    const player = OvenPlayer.create('player_id', {
        "autoStart": true,
        "mute": true,
        "autoReload": true,
        "autoReloadInterval": "2000",
        sources: [
            {
                label: 'label_for_webrtc',
                // Set the type to 'webrtc'
                type: 'webrtc',
                // Set the file to WebRTC Signaling URL with OvenMediaEngine 
                file: stream_url
            }
        ]
    });
}
