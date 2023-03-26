export function startPlayer(stream_url, stream_type) {

    // sources: [{ host: stream_url, application: 'app', stream: "stream", label: "WebRTC 1080P" }] });
    let player = OvenPlayer.create("player_id", { showBigPlayButton: true, autoReload: true, autoReloadInterval: 2000, autoStart: true, mute: false, disableSeekUI: true, timecode: false, volume: 1, title: "Press play - church broadcast live stream", controls: true, currentProtocolOnly: true, sources: [{ file: stream_url, type: stream_type , label: "livestream" }] });

    player.on('ready', function () {
        player.getConfig().systemText.api.error[501].message = 'Waiting for stream to start...';
    });
}
