export function startPlayer(stream_url, stream_type) {

    //let player = OvenPlayer.create("player", { autoStart: true, mute: false, disableSeekUI: true, timecode: false, volume: 0,  title: "Church broadcast live stream (offline)", controls: true, currentProtocolOnly: true, sources: [{ host: stream_url, application: 'app', stream: "stream", label: "WebRTC 1080P" }] });
    let player = OvenPlayer.create("player", { autoStart: true, mute: false, disableSeekUI: true, timecode: false, volume: 0, title: "Church broadcast live stream (offline)", controls: true, currentProtocolOnly: true, sources: [{ file: stream_url, type: stream_type , label: "livestream" }] });

    player.on('ready', function () {
        player.getConfig().systemText.api.error[501].message = 'Waiting for live stream...';
    });



    //const player = OvenPlayer.create('player_id', {
    //    "autoStart": true,
    //    "mute": true,
    //    "autoReload": true,
    //    "autoReloadInterval": "2000",
    //    sources: [
    //        {
    //            label: 'label_for_webrtc',
    //            type: stream_type,
    //            file: stream_url
    //        }
    //    ]
    //});
}
