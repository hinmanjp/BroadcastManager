﻿using BroadcastManager2.Components;
using CliWrap;
using Microsoft.AspNetCore.Components;
using Microsoft.Data.Sqlite;
using OBSWebsocketDotNet;
using Renci.SshNet;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.Sockets;
using System.Net;
using Vultr.Enums;
using Vultr.Models;

namespace BroadcastManager2.Pages
{
    public partial class Manager : ComponentBase
    {

        private OBSWebsocket obs = new OBSWebsocket();
        private TaskCompletionSource<bool>? tcs = null;
        private StreamViewer? localViewer;
        //private RemoteViewer? remoteViewer = new RemoteViewer();
        private bool showRemote = false;
        private DnsHelper.DnsSplit remoteDnsSplit;
        private SetupTimer? sTimer;

        //private string adminPW = "";
        private string appDir = "";
        private string localServerDnsName = "";
        private string remoteServerDnsName = "";

        private string obsKey = "";
        private string obsUrl = "";
        private int remoteRtmpPort = 1936;
        private string rtspServerDownloadUrl = "";
        private bool showLocalPlayer = true;
        private string sshPrivateFile = "";
        private string sshPublicFile = "";
        private string vultrKey = "";
        private string vultrUrl = "https://api.vultr.com/v2/";
        private int waitForObsConnection;

        private bool obs_connected;
        private string alert_msg = "";

        private string width = "90%";
        private string height = "auto";

        [Inject] NavigationManager? NavManager { get; set; }
        [Inject] IConfiguration? configuration { get; set; }
        [Inject] IHttpClientFactory? httpClientFactory { get; set; }


        protected override async Task OnInitializedAsync()
        {


            sTimer = new SetupTimer();

            localViewer = new StreamViewer();

            if ( SharedState.CurrentState is null )
                SharedState.CurrentState = SharedState.BroadcastState.stopped;
            obs_connected = obs.IsConnected;
            obs.Connected += onObsConnect;


            ProcessModule? mainModule = Process.GetCurrentProcess().MainModule;
            if ( mainModule != null )
                appDir = Path.GetDirectoryName( mainModule.FileName ) ?? "";

            // load config settings from appsettings.json
            localServerDnsName = configuration["LocalServerDnsName"] ?? "";
            remoteServerDnsName = configuration["RemoteServerDnsName"] ?? "";
            obsKey = configuration["ObsApiKey"] ?? "";
            obsUrl = configuration["ObsUrl"] ?? "ws://127.0.0.1:4455";
            rtspServerDownloadUrl = configuration["RtspServerDownloadUrl"] ?? "";
            sshPrivateFile = configuration["SshPrivateKeyFile"] ?? "BroadcastManager_ssh_key";
            sshPublicFile = configuration["SshPublicKeyFile"] ?? "BroadcastManager_ssh_key.pub";
            vultrKey = configuration["VultrApiKey"] ?? "";
            waitForObsConnection = Convert.ToInt32( configuration["WaitSecsForObsConnection"] ?? "10" );

            remoteDnsSplit = DnsHelper.SplitDnsName( remoteServerDnsName );

            if ( !Path.IsPathRooted( sshPrivateFile ) )
                sshPrivateFile = Path.Combine( appDir, sshPrivateFile );

            if ( !Path.IsPathRooted( sshPublicFile ) )
                sshPublicFile = Path.Combine( appDir, sshPublicFile );

            await Task.Delay( 0 );
        }

        protected override async Task OnAfterRenderAsync( bool firstRender )
        {
            if ( firstRender )
            {
                if ( sTimer != null )
                    sTimer.HideTimer();

                if ( SharedState.CurrentState == SharedState.BroadcastState.running || SharedState.CurrentState == SharedState.BroadcastState.paused )
                {   // should probably check the state of OBS, nginx, OvenMediaEngine as well, but...
                    await StartLocalPlayer();
                    showLocalPlayer = true;
                    Refresh();
                }
            }
            await base.OnAfterRenderAsync( firstRender );
        }

        private void onObsConnect( object? sender, EventArgs e )
        {
            obs_connected = obs.IsConnected;
            tcs?.TrySetResult( true );
            //Refresh();
        }

        private async Task OnBtnStart()
        {
            if ( !ChangeState( SharedState.BroadcastState.starting ) )
            {
                return;
            }
            if ( sTimer != null )
            {
                sTimer.ShowTimer();
                sTimer.StartTimer();
            }
            // REALLY should report a problem on the login page and just not work if a private key has not been provided / configured. App needs the private key of the Local Server to work properly.
            // OR - if we have the local username & password, should sudo to root and install the public key to allow ssh after key is created.
            // generate an ssh key pair if either doesn't exist
            if ( !File.Exists( sshPrivateFile ) || !File.Exists( sshPublicFile ) )
            {
                var sshResult = Cli.Wrap("$(/usr/bin/which ssh-keygen)")
    .WithArguments($"-q -N '' -t ed25519 -C '{sshPrivateFile}' -f {Path.Combine(appDir, sshPrivateFile)}  <<<y >/dev/null 2>&1")
    .WithWorkingDirectory(appDir);
            }


            // check to see if we already have a record of a remote broadcast server running
            // if we do - check to see if the VULTR API shows it is running
            // if it is, check to see if we can connect to it
            // destroy it if we can't connect
            // start a new one if it doesn't exist

            var viInfo = await StartVultrVm();
            //VultrInstanceInfo viInfo = new VultrInstanceInfo() { PublicIPv4 = "10.20.30.40", InstanceID = "dummy_id", InstanceLabel = "dummy_label" };

            // save the id, ip address, and label of the new instance
            SaveRemoteVmInfo( viInfo );

            // update DNS records so that the remote server can be found
            var dns = new UpdateCloudflareDNS(configuration["CloudFlareTokenKey"] ?? "");
            var dnsUpdateResult = await dns.UpdateDnsAsync(remoteDnsSplit.ZoneName, remoteDnsSplit.RecordName, viInfo.PublicIPv4, new CancellationToken());


            // wait for remote server setup & 1st reboot to complete - poll & sleep
            int readyCount = 0;
            alert_msg = $"Waiting for remote server to finish initial startup";
            Refresh();

            do
            {
                await WaitForSshRunning( viInfo.PublicIPv4 );
                readyCount += 1;
            }
            while ( readyCount < 5 );

            alert_msg = "Finishing configuration of remote server";
            Refresh();
            SetupRemoteBroadcastServer( viInfo.PublicIPv4 );

            if ( !(await WaitForHttpRunning( viInfo.PublicIPv4 )) )
            {
                alert_msg = "Remote server is NOT online!";
                Refresh();
            }

            // connect to the obs websocket
            if ( !obs.IsConnected )
                await ConnectToObs();

            //    https://gist.github.com/steinwaywhw/a4cd19cda655b8249d908261a62687f8

            // make sure obs scene selected is camera? Or...
            obs.SetCurrentProgramScene( "Camera" );

            // make sure obs is streaming
            if ( !(obs.GetStreamStatus().IsActive || obs.GetStreamStatus().IsReconnecting) )
                obs.StartStream();

            await Task.Delay( 1000 ); // wait 1 second for the stream to start before staring the local stream player
            await StartLocalPlayer();

            // nginx on local server won't push to new remote server until it is restarted.
            await EnableRtmpPush( viInfo.PublicIPv4 );


            alert_msg = "Waiting for the remote stream to start playing";
            Refresh();

            await WaitForHlsStartAsync( viInfo.PublicIPv4 );
            //if (remoteViewer != null)
            //    await remoteViewer.StartPlayerAsync($"https://{remoteServerDnsName}'/stream/hls/sac1.m3u8'");
            showRemote = true;
            Refresh();


            ChangeState( SharedState.BroadcastState.running );

            if ( sTimer != null )
            {
                sTimer.StopTimer();
                //sTimer.ResetTimer();
                //sTimer.HideTimer();
            }
            alert_msg = "";
            Refresh();
            await Task.Delay( 0 );
        }

        private async Task OnPause()
        {
            if ( !ChangeState( SharedState.BroadcastState.paused ) )
            { return; }

            if ( !obs.IsConnected )
                await ConnectToObs();

            if ( obs.IsConnected )
            {
                obs.SetCurrentProgramScene( "Paused" );
            }

            await Task.Delay( 0 );
        }

        private async Task OnResume()
        {
            if ( !ChangeState( SharedState.BroadcastState.running ) )
            { return; }

            if ( !obs.IsConnected )
                await ConnectToObs();

            if ( obs.IsConnected )
            {
                obs.SetCurrentProgramScene( "Camera" );
            }
            Refresh();
            await Task.Delay( 0 );
        }

        private async Task OnStop()
        {
            if ( !ChangeState( SharedState.BroadcastState.stopping ) )
            { return; }

            if ( !obs.IsConnected )
                await ConnectToObs();

            if ( obs.IsConnected )
            {
                obs.SetCurrentProgramScene( "Black" );
                await Task.Delay( 1500 );  // let the back screen show for a bit before disconnecting

                if ( obs.GetRecordStatus().IsRecording )
                    obs.StopRecord();
                if ( obs.GetStreamStatus().IsActive || obs.GetStreamStatus().IsReconnecting )
                    obs.StopStream();
            }

            if ( localViewer != null )
                localViewer.HidePlayer();

            showRemote = false;

            await DisableRtmpPush();
            alert_msg = "Waiting for remote viewers to finish before removing the remote server...";
            // should show a countdown timer here...
            Refresh();

            // destroy remote server
            // but not right away. Remote viewers need to finish watching...
            // should really make all of this cancelable if somebody wants to start again before the vm is destroyed.

            int.TryParse( configuration["ShutdownDelaySeconds"], out int delaySeconds );
            // 5 minute default delay if nothing is set;
            if ( delaySeconds == 0 ) delaySeconds = 300;
            await Task.Delay( delaySeconds * 1000 );
            await StopVultrVm();

            // remove dns records
            var dns = new UpdateCloudflareDNS(configuration["CloudFlareTokenKey"] ?? "");
            var deleteDnsResult = await dns.DeleteDnsAsync(remoteDnsSplit.ZoneName, remoteDnsSplit.RecordName, new CancellationToken());

            ChangeState( SharedState.BroadcastState.stopped );

            alert_msg = string.Empty;
            Refresh();

            await Task.Delay( 0 );
        }

        private bool ChangeState( SharedState.BroadcastState NewState )
        {
            // don't even bother to lock if the app state has already changed
            if ( !CheckState( NewState ) )
                return false;

            lock ( SharedState.LockObj )// don't want to hold a lock for the whole startup process - just make state hasn't changed by someone else already, and then continue or exit.
            {
                if ( CheckState( NewState ) )
                    SharedState.CurrentState = NewState;
                else
                    return false;
            }

            Refresh();

            return true;
        }

        private bool CheckState( SharedState.BroadcastState NewState )
        {
            if ( NewState == SharedState.BroadcastState.starting && SharedState.CurrentState is not null && SharedState.CurrentState != SharedState.BroadcastState.stopped )
                return false; // somebody else pressed the start or resume button already. Don't need to repeat what they've started.

            if ( NewState == SharedState.BroadcastState.stopping && SharedState.CurrentState is not null && (SharedState.CurrentState == SharedState.BroadcastState.stopped || SharedState.CurrentState == SharedState.BroadcastState.stopping) )
                return false; // somebody else pressed the stop button already. Don't need to repeat what they've started.

            if ( NewState == SharedState.BroadcastState.paused && SharedState.CurrentState is not null && SharedState.CurrentState == SharedState.BroadcastState.paused )
                return false; // somebody else pressed the start button already. Don't need to repeat what they've started.

            return true;
        }


        private async Task ConnectToObs()
        {
            try
            {
                if ( !obs.IsConnected )
                    await obs.ConnectAsync( obsUrl, obsKey );
            }
            catch ( Exception ex )
            {
                alert_msg = "Connection to OBS service failed : " + ex.Message;
            }


            tcs = new TaskCompletionSource<bool>();
            var obs_connect = tcs.Task;
            int timeout = waitForObsConnection * 1000;
            if ( !(await Task.WhenAny( obs_connect, Task.Delay( timeout ) ) == obs_connect) )
            {
                // obs didn't start in time!
                alert_msg = "The OBS socket didn't connect in time!";
                return;
            }
        }

        private async Task DisableRtmpPush()
        {

            PrivateKeyFile keyFile = new PrivateKeyFile(sshPrivateFile);
            var auth = new PrivateKeyAuthenticationMethod(username: "root", keyFiles: keyFile);
            var ci = new Renci.SshNet.ConnectionInfo(host: localServerDnsName, username: "root", authenticationMethods: auth);
            using ( SshClient sshClient = new SshClient( ci ) )
            {
                sshClient.Connect();
                if ( sshClient.IsConnected )
                {
                    sshClient.RunCommand( "systemctl stop stunnel@rtmp_out" );
                    //sshClient.RunCommand( "sed -ri 's/(^\\s*)(push rtmp:\\/\\/.*\\/live\\/sac1.*)/\\1#\\2/' /etc/nginx/nginx.conf ; systemctl reload nginx " );
                    sshClient.Disconnect();
                }
            }

            await Task.Delay( 0 );
        }

        private async Task EnableRtmpPush( string RemoteIP )

        {
            // ssh to the local server as entered in config (should be localhost in prod)
            // rewrite the nginx conf to push to the new remote server based on ip. Eventually might need to generate a self signed certificate for the IP and trust it. If nginx rtmp can ever by encrypted.
            // reload nginx (systemctl reload nginx)
            var keyFile = new PrivateKeyFile(sshPrivateFile);
            var auth = new PrivateKeyAuthenticationMethod(username: "root", keyFiles: keyFile);
            var ci = new Renci.SshNet.ConnectionInfo(host: localServerDnsName, username: "root", authenticationMethods: auth);
            using ( SshClient sshClient = new SshClient( ci ) )
            {
                sshClient.Connect();
                if ( sshClient.IsConnected )
                {
                    sshClient.RunCommand( @$"sed -ri 's/^\s*connect\s*=\s*.*:{remoteRtmpPort}/connect = {RemoteIP}:{remoteRtmpPort}/' /etc/stunnel/rtmp_out.conf ; systemctl start stunnel@rtmp_out " );
                    //sshClient.RunCommand( $"sed -ri 's/^#?(\\s*)#?push rtmp:\\/\\/.*\\/live\\/sac1.*/\\1push rtmp:\\/\\/{RemoteIP}:1936\\/live\\/sac1?authkey=This-is-a-place-holder;/' /etc/nginx/nginx.conf ; systemctl reload nginx " );
                    sshClient.Disconnect();
                }
            }
            await Task.Delay( 0 );
        }


        private void Refresh()
        {
            _ = InvokeAsync( () =>
            {
                StateHasChanged();
            } );
        }


        private void SaveRemoteVmInfo( VultrInstanceInfo viInfo )
        {

            using ( var connection = new SqliteConnection( "Data Source=broadcast.db" ) )
            {
                connection.Open(); // will create the db file if it doesn't exist
                var command = connection.CreateCommand();

                command.CommandText = @"SELECT name 
FROM
  sqlite_schema
WHERE
  type ='table' 
  AND name = 'remote_vm'";

                if ( command.ExecuteScalar() is null )
                {
                    command.CommandText = "CREATE TABLE remote_vm (vm_id TEXT, vm_ip TEXT, vm_label TEXT);";
                    command.ExecuteNonQuery();
                }

                command.CommandText =
                    @"INSERT INTO remote_vm (vm_id, vm_ip, vm_label) SELECT $id, $ip, $label;";

                command.Parameters.AddWithValue( "$id", viInfo.InstanceID );
                command.Parameters.AddWithValue( "$ip", viInfo.PublicIPv4 );
                command.Parameters.AddWithValue( "$label", viInfo.InstanceLabel );
                command.ExecuteNonQuery();
            }
        }

        private void SetupRemoteBroadcastServer( string serverIP )
        {
            // string authServiceRemoteDir = "/opt/broadcastAuth";
            string sslCertPath = configuration["SslCertPath"] ?? "";
            string sslKeyPath = configuration["SslKeyPath"] ?? "";
            string sslPfxPath = configuration["SslPfxPath"] ?? "";
            string remoteSetupScript = configuration["RemoteSetupScript"] ?? "";
            string broadcastAuthZip = configuration["BroadcastAuthZip"] ?? "";
            string sslKeyDestPath = "/etc/ssl/willowbrook-ward.org.key";
            string sslPfxDestPath = "/etc/ssl/willowbrook-ward.org.pfx";

            if ( String.IsNullOrWhiteSpace( sshPrivateFile ) )
                throw new ArgumentException( "sshPrivateFile must be specified and valid" );

            if ( String.IsNullOrWhiteSpace( sslCertPath ) )
                throw new ArgumentException( "sslCertPath must be specified and valid" );

            if ( String.IsNullOrWhiteSpace( sslKeyPath ) )
                throw new ArgumentException( "sslKeyPath must be specified and valid" );

            if ( !File.Exists( sshPrivateFile ) )
                throw new FileNotFoundException( $"ssh key file not found at path {sshPrivateFile}" );

            if ( !File.Exists( sslCertPath ) )
                throw new FileNotFoundException( $"SSL certificate file not found at path {sslCertPath}" );

            if ( !File.Exists( sslKeyPath ) )
                throw new FileNotFoundException( $"SSL key file not found at path {sslKeyPath}" );

            if ( !File.Exists( sslPfxPath ) )
                throw new FileNotFoundException( $"SSL key file not found at path {sslPfxPath}" );

            if ( !File.Exists( remoteSetupScript ) )
                throw new FileNotFoundException( $"Remote setup script not found at path {remoteSetupScript}" );

            if ( !File.Exists( broadcastAuthZip ) )
                throw new FileNotFoundException( $"Broadcast Authorization zip file not found at path {broadcastAuthZip}" );

            var keyFile = new PrivateKeyFile(sshPrivateFile);
            var auth = new PrivateKeyAuthenticationMethod(username: "root", keyFiles: keyFile);
            var ci = new Renci.SshNet.ConnectionInfo(host: serverIP, username: "root", authenticationMethods: auth);


            using ( ScpClient scpClient = new ScpClient( ci ) )
            using ( StreamReader appReader = new StreamReader( broadcastAuthZip ) )
            using ( StreamReader certReader = new StreamReader( sslCertPath ) )
            using ( StreamReader keyReader = new StreamReader( sslKeyPath ) )
            using ( StreamReader pfxReader = new StreamReader( sslPfxPath ) )
            using ( StreamReader remoteSetupReader = new StreamReader( remoteSetupScript ) )
            {
                scpClient.Connect();
                scpClient.Upload( source: appReader.BaseStream, path: "/tmp/broadcastAuth.zip" );
                scpClient.Upload( source: certReader.BaseStream, path: "/etc/ssl/willowbrook-ward.org.full-chain.crt" );
                scpClient.Upload( source: keyReader.BaseStream, path: sslKeyDestPath );
                scpClient.Upload( source: pfxReader.BaseStream, path: sslPfxDestPath );
                scpClient.Upload( source: remoteSetupReader.BaseStream, path: "/tmp/remote_setup.sh" );
                // upload javascript files

                scpClient.Disconnect();
            }

            using SshClient sshClient = new SshClient( ci );
            sshClient.Connect();
            if ( sshClient.IsConnected )
            {
                sshClient.RunCommand( "chmod +x /tmp/remote_setup.sh ; /tmp/remote_setup.sh" );
                sshClient.Disconnect();
            }
        }

        private async Task StartLocalPlayer()
        {
            if ( localViewer is not null )
                await localViewer.StartPlayerAsync( StreamUrl: $"wss://{localServerDnsName}:3334/app/sac1"
                                                 , sourceType: StreamViewer.SourceType.webrtc );
        }

        private async Task<VultrInstanceInfo> StartVultrVm()
        {
            // need to read the public key data from the public key file.
            string sshPublicKey = File.ReadAllText(sshPublicFile);

            Vultr.API.VultrClient vc = new Vultr.API.VultrClient(apiKey: vultrKey, apiURL: vultrUrl);

            var sshList = vc.SSHKey.GetSSHKeys();
            var sshKey = sshList.SshKeys.Where(s => s.ssh_key == sshPublicKey).FirstOrDefault(new Ssh_Key());

            if ( string.IsNullOrEmpty( sshKey.id ) )
                sshKey = vc.SSHKey.CreateSSHKey( name: "ChurchBroadcastManager", ssh_key: sshPublicKey ).SshKeys.FirstOrDefault();

            if ( sshKey is null ) // just go with an empty key if we don't have one...
                sshKey = new Ssh_Key();

            var regions = vc.Region.GetRegions();
            string[] excludedRegions = new[] { "atl", "ewr", "hnl", "mia", "ord", "lax" };
            var usRegions = regions.Regions.Where(r => r.country == "US" && !excludedRegions.Any(e => r.id.Contains(e))).ToList();

            var plans = vc.Plan.GetPlans(Vultr.Enums.PlanTypes.all);
            var sizedPlans = plans.Plans.Where(p => p.vcpu_count >= 2 && p.ram >= 4000 && p.type == PlanTypes.vhf.ToString()).ToList();

            var r2 = sizedPlans.FindAll(p => usRegions.Any(r => p.locations.Contains(r.id))).OrderBy(p => p.monthly_cost).ToArray();
            var plan1 = r2.FirstOrDefault(new Plan());
            var regionID = plan1.locations.Where(p => usRegions.Any(r => p.Contains(r.id))).FirstOrDefault("");

            var os_result = vc.OperatingSystem.GetOperatingSystems();
            var os = os_result.osList.Where(o => o.family == "ubuntu" && o.arch == "x64" && o.name.Contains("LTS")).ToList().OrderBy(o => o.name).ToList().LastOrDefault(new Os());

            var script = vc.StartupScript.GetStartupScripts().StartupScripts.Where(s => s.name == "test").FirstOrDefault(new Startup_Scripts());

            var instanceInfo = vc.Instance.CreateInstance(Label: "Broadcaster", Hostname: "broadcaster", RegionID: regionID, PlanID: plan1.id, SourceID: os.id.ToString(), Source: Vultr.Clients.InstanceClient.SourceType.os, ScriptID: script.id, SshKeyIDs: new[] { sshKey.id });

            VultrInstanceInfo viInfo = new VultrInstanceInfo() { InstanceID = instanceInfo.Instances[0].id };
            // give the provider a few seconds to allocate an IP address for the remote server
            int loopCount = 0;
            do
            {
                alert_msg = "Waiting for IP address allocation on the remote server";
                Refresh();
                loopCount += 1;
                await Task.Delay( 2000 );
                var instanceDetail = vc.Instance.GetInstance(viInfo.InstanceID);
                viInfo.PublicIPv4 = instanceDetail.Instances[0].main_ip;
                viInfo.InstanceLabel = instanceDetail.Instances[0].label;
            }
            while ( viInfo.PublicIPv4 == "0.0.0.0" || loopCount >= 90 );

            if ( viInfo.PublicIPv4 is not null && viInfo.PublicIPv4 != "0.0.0.0" )
                alert_msg = "Remote server IP address has been allocated.";
            else
                alert_msg = "Failed to get an IP address for the remote server before timing out.";
            Refresh();

            //var instanceList = vc.Instance.ListInstances();
            //var bcastInstance = instanceList.Instances.Where(o => o.label == "Broadcaster").FirstOrDefault(new Instance());

            //var instanceDelResult = vc.Instance.DeleteInstance(bcastInstance.id);
            return viInfo;
        }

        private async Task StopVultrVm()
        {
            Vultr.API.VultrClient vc = new Vultr.API.VultrClient(apiKey: vultrKey, apiURL: vultrUrl);
            // lookup list of vm ids

            List<string> idList = new List<string>();
            using ( var connection = new SqliteConnection( "Data Source=broadcast.db" ) )
            {
                connection.Open(); // will create the db file if it doesn't exist
                var command = connection.CreateCommand();

                command.CommandText = @"SELECT name FROM sqlite_schema WHERE type ='table' AND name = 'remote_vm'";

                if ( command.ExecuteScalar() is not null )
                {
                    command.CommandText = "SELECT vm_id FROM remote_vm;";
                    using ( var reader = command.ExecuteReader() )
                    {
                        while ( reader.Read() )
                        {
                            var id = reader.GetString(0);
                            idList.Add( id );
                        }
                    }
                    foreach ( string id in idList )
                    {
                        var delResult = vc.Instance.DeleteInstance(id);
                    }
                    command.CommandText = "DELETE FROM remote_vm;";
                    try
                    {
                        await command.ExecuteNonQueryAsync();
                    }
                    catch ( Exception ex )
                    {
                        var e1 = ex;
                    }
                }
            }

            await Task.Delay( 0 );
        }

        private struct VultrInstanceInfo
        {
            public string InstanceID { get; set; }

            [DefaultValue( "0.0.0.0" )]
            public string PublicIPv4 { get; set; }
            public string InstanceLabel { get; set; }
        }

        private async Task WaitForHlsStartAsync( string serverIP )
        {
            var keyFile = new PrivateKeyFile(sshPrivateFile);
            var auth = new PrivateKeyAuthenticationMethod(username: "root", keyFiles: keyFile);
            var ci = new Renci.SshNet.ConnectionInfo(host: serverIP, username: "root", authenticationMethods: auth);
            try
            {
                using ( SshClient sshClient = new SshClient( ci ) )
                {
                    sshClient.Connect();
                    if ( sshClient.IsConnected )
                    {
                        sshClient.RunCommand( "/tmp/hls_check.sh" );
                        sshClient.Disconnect();
                    }
                }
            }
            catch ( Exception ex )
            {
                var e1 = ex;  // not TOO concerned if this fails.  
            }
            await Task.Delay( 0 );
        }

        private async Task<bool> WaitForHttpRunning( string ipv4Address, int currentCount = 0, int maxCount = 300 )
        {
            var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, $"https://{ipv4Address}/alive.html");

            //var httpClient = httpClientFactory.CreateClient();
            var handler = new HttpClientHandler()
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            };

            var httpClient = new HttpClient(handler);
            httpClient.Timeout = new TimeSpan( 0, 0, 60 );
            HttpResponseMessage? httpResponseMessage = null;

            try
            {
                httpResponseMessage = await httpClient.SendAsync( httpRequestMessage );
            }
            catch ( Exception ex )
            {
                var e1 = ex;
            }

            if ( httpResponseMessage != null && httpResponseMessage.IsSuccessStatusCode )
                return true;
            else if ( currentCount >= maxCount )
                return false;
            else
                return await WaitForHttpRunning( ipv4Address, currentCount + 1, maxCount );

        }

        private async Task<bool> WaitForSshRunning( string ipv4Address, int currentCount = 0, int maxCount = 300 )
        {
            IPAddress ipAddress = IPAddress.Parse(ipv4Address);
            IPEndPoint ipEndPoint = new(ipAddress, 22);

            using Socket client = new(ipEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            {
                IAsyncResult? result = null;
                try
                {

                    result = client.BeginConnect( ipEndPoint, null, null );
                }
                catch ( Exception ex )
                {
                    var e1 = ex;
                    return await WaitForSshRunning( ipv4Address, currentCount + 1, maxCount );
                }

                if ( result != null )
                {
                    _ = result.AsyncWaitHandle.WaitOne( 2000, true );
                    if ( client.Connected )
                    {
                        client.EndConnect( result );
                        client.Close();
                        return true;
                    }
                }
                // NOTE, MUST CLOSE THE SOCKET
                client.Close();
            }

            await Task.Delay( 500 );
            return await WaitForSshRunning( ipv4Address, currentCount + 1, maxCount );


            //bool isReady = false;
            //PrivateKeyFile keyFile = new PrivateKeyFile(sshPrivateFile);
            //Renci.SshNet.PrivateKeyAuthenticationMethod auth = new PrivateKeyAuthenticationMethod(username: "root", keyFiles: keyFile);
            //ConnectionInfo ci = new ConnectionInfo(host: ipv4Address, username: "root", authenticationMethods: auth);
            //ci.Timeout = TimeSpan.FromSeconds(2);

            //using (SshClient sshClient = new SshClient(ci))
            //{

            //    SshCommand? cmdResult = null;
            //    if (!sshClient.IsConnected)
            //        try
            //        {
            //            await sshClient.;
            //        }
            //        catch (Exception ex)
            //        {
            //            // won't be able to connect until the remote server is fully provisioned. Don't need to report the failures.
            //            var e1 = ex;
            //        }

            //    if (sshClient.IsConnected)
            //    {
            //        cmdResult = sshClient.RunCommand("cat /started");
            //    }

            //    if (cmdResult is not null && cmdResult.ExitStatus == 0)
            //        isReady = true;

            //    if (sshClient.IsConnected)
            //        sshClient.Disconnect();

            //}
            // await Task.Delay( 0 );
            //return isReady;
        }


    }
}