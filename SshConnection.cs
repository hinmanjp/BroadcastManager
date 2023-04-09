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
            var sshKeyFile = new PrivateKeyFile( AppSettings.SshPrivateKeyFile );
            var sshAuthMethod = new PrivateKeyAuthenticationMethod( username: "root", keyFiles: sshKeyFile );
            var sshConInfo = new Renci.SshNet.ConnectionInfo( host: ServerAddress, username: "root", authenticationMethods: sshAuthMethod );
            return sshConInfo;
        }
    }
}
