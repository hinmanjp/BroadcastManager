using Renci.SshNet;
//using static MudBlazor.CategoryTypes;
using System.Diagnostics;

namespace BroadcastManager2
{
    public class Ssh
    {
        public static Renci.SshNet.SshClient GetSshClient(string ServerAddress)
        {
            return new SshClient( GetConnectionInfo(ServerAddress) );
        }

        public static Renci.SshNet.ScpClient GetScpClient( string ServerAddress )
        { 
            return new ScpClient( GetConnectionInfo(ServerAddress) );
        }

        private static Renci.SshNet.ConnectionInfo GetConnectionInfo(string ServerAddress) 
        {

            string appDir = "";
            string sshPrivateFile = "";
            string sshPublicFile = "";

            ProcessModule? mainModule = Process.GetCurrentProcess().MainModule;
            if ( mainModule != null )
                appDir = Path.GetDirectoryName( mainModule.FileName ) ?? "";

            if ( !Path.IsPathRooted( sshPrivateFile ) )
                sshPrivateFile = Path.Combine( appDir, sshPrivateFile );

            if ( !Path.IsPathRooted( sshPublicFile ) )
                sshPublicFile = Path.Combine( appDir, sshPublicFile );

            var sshKeyFile = new PrivateKeyFile( sshPrivateFile );
            var sshAuthMethod = new PrivateKeyAuthenticationMethod( username: "root", keyFiles: sshKeyFile );
            var sshConInfo = new Renci.SshNet.ConnectionInfo( host: ServerAddress, username: "root", authenticationMethods: sshAuthMethod );
            return sshConInfo;
        }
    }
}
