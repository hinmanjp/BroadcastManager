using BroadcastManager2;
using BroadcastManager2.Components;
using CliWrap;
using MudBlazor;
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
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.AspNetCore.Components.Authorization;
using System.Data;
using Vultr.API;
using static MudBlazor.Colors;
using System.Runtime.Intrinsics.X86;
using System.Diagnostics.Eventing.Reader;
using System.Reflection;

namespace BroadcastManager2.Pages
{
    public partial class Manager : ComponentBase
    {
        [Inject] AuthenticationStateProvider? auth { get; set; }
        [Inject] IConfiguration? configuration { get; set; }
        [Inject] IHttpClientFactory? httpClientFactory { get; set; }

        private static event EventHandler<StateChangedArgs> OnStateChanged;

        private OBSWebsocket obs = new OBSWebsocket();
        private TaskCompletionSource<bool>? tcs = null;
        private StreamViewer? localViewer;
        //private RemoteViewer? remoteViewer = new RemoteViewer();
        private bool showRemote = false;
        private DnsHelper.DnsSplit remoteDnsSplit;
        private SetupTimer? sTimer;
        private ClaimsPrincipal user;
        private Vultr.Models.Instance remoteVM;
 

        //private string adminPW = "";
        private string vultrVmLabel = "";
        private string appDir = "";
        private string cloudflareTokenKey = "";
        private string localServerDnsName = "";
        private string remoteServerDnsName = "";

        private string obsKey = "";
        private string obsUrl = "";
        private int remoteRtmpPort = 1936;
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
        private bool showWaitingMsgOnStart;

        public Manager( AuthenticationStateProvider? auth, IConfiguration? configuration, IHttpClientFactory? httpClientFactory, OBSWebsocket obs, TaskCompletionSource<bool>? tcs, StreamViewer? localViewer, bool showRemote, DnsHelper.DnsSplit remoteDnsSplit, SetupTimer? sTimer, ClaimsPrincipal user, string appDir, string localServerDnsName, string remoteServerDnsName, string obsKey, string obsUrl, int remoteRtmpPort, bool showLocalPlayer, string sshPrivateFile, string sshPublicFile, string vultrKey, string vultrUrl, int waitForObsConnection, bool obs_connected, string alert_msg, string width, string height, Vultr.Models.Instance remoteVM )
        {
            this.auth = auth;
            this.configuration = configuration;
            this.httpClientFactory = httpClientFactory;
            this.obs = obs;
            this.tcs = tcs;
            this.localViewer = localViewer;
            this.showRemote = showRemote;
            this.remoteDnsSplit = remoteDnsSplit;
            this.sTimer = sTimer;
            this.user = user;
            this.appDir = appDir;
            this.localServerDnsName = localServerDnsName;
            this.remoteServerDnsName = remoteServerDnsName;
            this.obsKey = obsKey;
            this.obsUrl = obsUrl;
            this.remoteRtmpPort = remoteRtmpPort;
            this.showLocalPlayer = showLocalPlayer;
            this.sshPrivateFile = sshPrivateFile;
            this.sshPublicFile = sshPublicFile;
            this.vultrKey = vultrKey;
            this.vultrUrl = vultrUrl;
            this.waitForObsConnection = waitForObsConnection;
            this.obs_connected = obs_connected;
            this.alert_msg = alert_msg;
            this.width = width;
            this.height = height;
            this.remoteVM = remoteVM;
        }

        public Manager() { }

        protected override async Task OnInitializedAsync()
        {
            OnStateChanged += ( o, e ) =>
            {
                // Since we're not necessarily on the thread that has proper access to the renderer context
                // we need to use the InvokeAsync() method, which takes care of running our code on the right thread.
                this.InvokeAsync( () => { this.StateHasChanged(); } );
            };

            var configValidation = ValidateAppSettings();
            if ( !configValidation.IsValid )
            {
                alert_msg = configValidation.Message;
                Refresh();
                return;
            }

            sTimer = new SetupTimer();

            localViewer = new StreamViewer();

            if ( SharedState.CurrentState is null )
                SharedState.CurrentState = SharedState.BroadcastState.stopped;
            obs_connected = obs.IsConnected;
            obs.Connected += onObsConnect;

            ProcessModule? mainModule = Process.GetCurrentProcess().MainModule;
            if ( mainModule != null )
                appDir = Path.GetDirectoryName( mainModule.FileName ) ?? "";

            remoteDnsSplit = DnsHelper.SplitDnsName( remoteServerDnsName );

            if ( !Path.IsPathRooted( sshPrivateFile ) )
                sshPrivateFile = Path.Combine( appDir, sshPrivateFile );

            if ( !Path.IsPathRooted( sshPublicFile ) )
                sshPublicFile = Path.Combine( appDir, sshPublicFile );

            remoteVM = FindExistingRemoteServer();

            await Task.Delay( 0 );
            await base.OnInitializedAsync();
        }

        public bool IsNullable<T>( T value )
        {
            return Nullable.GetUnderlyingType( typeof( T ) ) != null;
        }

        protected override async Task OnAfterRenderAsync( bool firstRender )
        {
            await base.OnAfterRenderAsync( firstRender );
            var authstate = await auth.GetAuthenticationStateAsync();
            Refresh();
            user = authstate.User;
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

            
            if (string.IsNullOrWhiteSpace(remoteVM.id))
            {
                alert_msg = "Starting a new broadcast server instance";
                Refresh();
                remoteVM = await StartVultrVm();
            }
            //VultrInstanceInfo viInfo = new VultrInstanceInfo() { PublicIPv4 = "10.20.30.40", InstanceID = "dummy_id", InstanceLabel = "dummy_label" };

            

            // save the id, ip address, and label of the new instance
            SaveRemoteVmInfo( remoteVM );

            // update DNS records so that the remote server can be found
            var dns = new UpdateCloudflareDNS(cloudflareTokenKey);

            var dnsUpdateResult = await dns.UpdateDnsAsync(remoteDnsSplit.ZoneName, remoteDnsSplit.RecordName, remoteVM.main_ip, new CancellationToken());


            // wait for remote server setup & 1st reboot to complete - poll & sleep
            int readyCount = 0;
            alert_msg = $"Waiting for remote server to finish initial startup";
            Refresh();

            do
            {
                await WaitForSshRunning( remoteVM.main_ip );
                readyCount += 1;
            }
            while ( readyCount < 5 );

            alert_msg = "Finishing configuration of remote server";
            Refresh();
            SetupRemoteBroadcastServer( remoteVM.main_ip );

            if ( !(await WaitForHttpRunning( remoteVM.main_ip )) )
            {
                alert_msg = "Remote server is not yet online!";
                Refresh();
            }

            // make sure obs is actually running!
            using SshClient sshClient = Ssh.GetSshClient( localServerDnsName );
            sshClient.Connect();
            if ( sshClient.IsConnected )
            {
                sshClient.RunCommand( "if [ $(ps aux | grep - c \"[o]bs\") - eq 0 ]; then DISPLAY=:0 sudo --preserve-env=DISPLAY -u ***REMOVED*** obs &; sleep 5; fi" );
                sshClient.Disconnect();
            }

            // connect to the obs websocket
            if ( !obs.IsConnected )
                await ConnectToObs();

            // show a pre start screen, or the live camera, depending on ward level preference
            if ( showWaitingMsgOnStart )
                obs.SetCurrentProgramScene( "WaitForStart" );
            else
                obs.SetCurrentProgramScene( "Camera" );

            // make sure obs is streaming
            if ( !(obs.GetStreamStatus().IsActive || obs.GetStreamStatus().IsReconnecting) )
                obs.StartStream();

            await Task.Delay( 1000 ); // wait 1 second for the stream to start before staring the local stream player
            await StartLocalPlayer();

            // nginx on local server won't push to new remote server until it is restarted.
            await EnableRtmpPush( remoteVM.main_ip );


            alert_msg = "Waiting for the remote stream to start playing";
            Refresh();

            await WaitForHlsStartAsync( remoteVM.main_ip );
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
            /*
            if ( !obs.IsConnected )
                await ConnectToObs();

            if ( obs.IsConnected )
            {
                obs.SetCurrentProgramScene( "Paused" );
            }
            */
            Refresh();
            await Task.Delay( 0 );
        }

        private async Task OnResume()
        {
            if ( !ChangeState( SharedState.BroadcastState.running ) )
            { return; }
            /*
            if ( !obs.IsConnected )
                await ConnectToObs();

            if ( obs.IsConnected )
            {
                obs.SetCurrentProgramScene( "Camera" );
            }
            */
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
                obs.SetCurrentProgramScene( "Finished" );
                await Task.Delay( 5000 );  // let the back screen show for a bit before disconnecting
                obs.SetCurrentProgramScene( "Black" );
                await Task.Delay( 1500 );

                if ( obs.GetRecordStatus().IsRecording )
                    obs.StopRecord();
                if ( obs.GetStreamStatus().IsActive || obs.GetStreamStatus().IsReconnecting )
                    obs.StopStream();
            }

            if ( localViewer != null )
                localViewer.HidePlayer();

            showRemote = false;

            await DisableStunnel();

            // shutdown the OBS instance
            using SshClient sshClient = Ssh.GetSshClient( localServerDnsName );
            sshClient.Connect();
            if ( sshClient.IsConnected )
            {
                sshClient.RunCommand( "killall obs" );
                sshClient.Disconnect();
            }

            ChangeState( SharedState.BroadcastState.stopped );

            alert_msg = string.Empty;
            Refresh();

            await Task.Delay( 0 );
        }

        private async Task OnShutdownRemote()
        {
            await StopVultrVm();

            // remove dns records
            var dns = new UpdateCloudflareDNS(AppSettings.CloudFlareTokenKey ?? "");
            var deleteDnsResult = await dns.DeleteDnsAsync(remoteDnsSplit.ZoneName, remoteDnsSplit.RecordName, new CancellationToken());

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
                    OnStateChanged.Invoke( null, new StateChangedArgs( NewState ) );
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

        private async Task DisableStunnel()
        {
            using ( SshClient sshClient = Ssh.GetSshClient( localServerDnsName ) )
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

        private Vultr.Models.Instance FindExistingRemoteServer()
        {
            var vc = new VultrClient(AppSettings.VultrApiKey ?? "");
            var instanceResult = vc.Instance.ListInstances();
            if ( instanceResult != null )
            {
                var instance = instanceResult.Instances.FirstOrDefault(i => !string.IsNullOrEmpty(i.label) && i.label == (AppSettings.VultrVmLabel ?? ""));
                if ( instance != null )
                {
                    return instance;
                }
            }

            return new Instance();
        }
        private void Refresh()
        {
            _ = InvokeAsync( () =>
            {
                StateHasChanged();
            } );
        }


        private void SaveRemoteVmInfo( Instance instance )
        {

            using ( var connection = new SqliteConnection( "Data Source=broadcast.db" ) )
            {
                connection.Open(); // will create the db file if it doesn't exist
                var command = connection.CreateCommand();

                command.CommandText = @"
SELECT count(*) 
  FROM sqlite_schema 
  WHERE TYPE='table'
    AND name='remote_vm'
    AND SQL LIKE '%vm_id TEXT PRIMARY KEY%';";

                var keyCount = (int?)command.ExecuteScalar();
                if (keyCount == 0)
                {
                    command.CommandText = @"SELECT name FROM sqlite_schema WHERE type ='table' AND name = 'remote_vm'";
                    var tblExists = command.ExecuteScalar();
                    command.CommandText = "";

                    if ( tblExists != null )
                        command.CommandText = "ALTER TABLE NAME remote_vm RENAME TO remote_vm_old;\n";

                    command.CommandText += "CREATE TABLE remote_vm (vm_id TEXT PRIMARY KEY, vm_ip TEXT, vm_label TEXT);\n";

                    if ( tblExists != null )
                        command.CommandText += "INSERT INTO remote_vm (vm_id, vm_ip, vm_label) SELECT * FROM remote_vm_old;\n";

                    command.ExecuteNonQuery();
                }

                command.CommandText =
@"INSERT INTO remote_vm (vm_id, vm_ip, vm_label) SELECT $id, $ip, $label
    ON CONFLICT(vm_id) DO UPDATE SET vm_ip = $ip;";

                command.Parameters.AddWithValue( "$id", instance.id );
                command.Parameters.AddWithValue( "$ip", instance.main_ip );
                command.Parameters.AddWithValue( "$label", instance.label);
                try { 
                command.ExecuteNonQuery();
                }
                catch (Exception ex) { var e1 = ex; }
            }
        }

        private void SetupRemoteBroadcastServer( string serverIP )
        {
            string sslKeyDestPath = $"/etc/ssl/{AppSettings.DomainName}.key";
            string sslPfxDestPath = $"/etc/ssl/{AppSettings.DomainName}.pfx";


            using ( ScpClient scpClient = Ssh.GetScpClient( serverIP ) )
            using ( StreamReader appReader = new StreamReader( AppSettings.BroadcastAuthZip ) )
            using ( StreamReader certReader = new StreamReader( AppSettings.SslCertPath ) )
            using ( StreamReader keyReader = new StreamReader( AppSettings.SslKeyPath ) )
            using ( StreamReader pfxReader = new StreamReader( AppSettings.SslPfxPath ) )
            using ( StreamReader remoteSetupReader = new StreamReader( AppSettings.RemoteSetupScript ) )
            {
                scpClient.Connect();
                scpClient.Upload( source: appReader.BaseStream, path: "/tmp/broadcastAuth.zip" );
                scpClient.Upload( source: certReader.BaseStream, path: $"/etc/ssl/{AppSettings.DomainName}.full-chain.crt" );
                scpClient.Upload( source: keyReader.BaseStream, path: sslKeyDestPath );
                scpClient.Upload( source: pfxReader.BaseStream, path: sslPfxDestPath );
                scpClient.Upload( source: remoteSetupReader.BaseStream, path: "/tmp/remote_setup.sh" );
                // upload javascript files

                scpClient.Disconnect();
            }

            using SshClient sshClient = Ssh.GetSshClient( serverIP );
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

        private async Task<Instance> StartVultrVm()
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

            var instanceInfo = vc.Instance.CreateInstance(Label: vultrVmLabel, Hostname: vultrVmLabel, RegionID: regionID, PlanID: plan1.id, SourceID: os.id.ToString(), Source: Vultr.Clients.InstanceClient.SourceType.os, ScriptID: script.id, SshKeyIDs: new[] { sshKey.id });

            // give the provider a few seconds to allocate an IP address for the remote server
            int loopCount = 0;
            do
            {
                alert_msg = "Waiting for IP address allocation on the remote server";
                Refresh();
                loopCount += 1;
                await Task.Delay( 2000 );
                instanceInfo = vc.Instance.GetInstance(instanceInfo.Instances[0].id);
            }
            while ( instanceInfo.Instances[0].main_ip == "0.0.0.0" || loopCount >= 90 );

            if ( instanceInfo.Instances[0].main_ip is not null && instanceInfo.Instances[0].main_ip != "0.0.0.0" )
                alert_msg = "Remote server IP address has been allocated.";
            else
                alert_msg = "Failed to get an IP address for the remote server before timing out.";
            Refresh();

            return instanceInfo.Instances[0];
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
        private ValidationResponse ValidateAppSettings()
        {
            var response = new ValidationResponse(true, "");
            var appSettings = new AppSettings();
            configuration.Bind( appSettings );

            foreach ( PropertyInfo pi in appSettings.GetType().GetProperties( BindingFlags.Public | BindingFlags.Instance ) )
            {
                if ( IsNullable( pi.GetType() ) )
                {
                    response.IsValid = false;
                    response.Message += $"{pi.Name} is required, but has not been set.<br/>";
                }
            }

            if ( !string.IsNullOrWhiteSpace( AppSettings.SshPrivateKeyFile ) && !File.Exists( AppSettings.SshPrivateKeyFile ) )
                response.Message += $"ssh key file not found at path {AppSettings.SshPrivateKeyFile}";

            if ( !string.IsNullOrWhiteSpace( AppSettings.SslCertPath ) && !File.Exists( AppSettings.SslCertPath ) )
                response.Message += $"SSL certificate file not found at path {AppSettings.SslCertPath}";

            if ( !string.IsNullOrWhiteSpace( AppSettings.SslKeyPath ) && !File.Exists( AppSettings.SslKeyPath ) )
                response.Message += $"SSL key file not found at path {AppSettings.SslKeyPath}";

            if ( !string.IsNullOrWhiteSpace( AppSettings.SslPfxPath ) && !File.Exists( AppSettings.SslPfxPath ) )
                response.Message += $"SSL key file not found at path {AppSettings.SslPfxPath}";

            if ( !string.IsNullOrWhiteSpace( AppSettings.RemoteSetupScript ) && !File.Exists( AppSettings.RemoteSetupScript ) )
                response.Message += $"Remote setup script not found at path {AppSettings.RemoteSetupScript}";

            if ( !string.IsNullOrWhiteSpace( AppSettings.BroadcastAuthZip ) && !File.Exists( AppSettings.BroadcastAuthZip ) )
                response.Message += $"Broadcast Authorization zip file not found at path {AppSettings.BroadcastAuthZip}";

            return response;
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
