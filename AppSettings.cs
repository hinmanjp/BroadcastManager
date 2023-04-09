using System.Net.NetworkInformation;

namespace BroadcastManager2
{
    public class AppSettings
    {

        public AppSettings() 
        { }

        private static string? domainName;
        //private static string sslCertPath = "";
        //private static string sslKeyPath = "";
        //private static string sslPfxPath = "";
        private static string? overrideSslCertPath;
        private static string? overrideSslKeyPath;
        private static string? overrideSslPfxPath;

        private static string? sslBasePath;

        public static Logging? Logging { get; set; }
        public static string AllowedHosts { get; set; } = "*";
        public static string? VultrApiKey { get; set; }
        public static string? VultrVmLabel { get; set; }
        public static string RemoteServerBaseName { get; set; } = "Broadcaster";
        public static string? SshPrivateKeyFile { get; set; }
        public static string? SshPublicKeyFile { get; set; }
        public static string? ObsApiKey { get; set; }
        public static string? OverrideObsUrl { get { return ObsUrl; } set { ObsUrl = string.IsNullOrWhiteSpace(value) ? ObsUrl : value; } }
        public static string ObsUrl { get; set; } = "ws://127.0.0.1:4455";
        //public static string? AdminUser { get; set; }
        //public static string? AdminPW { get; set; }
        public static string? CloudFlareTokenKey { get; set; }
        public static string? DomainName { 
            get { return domainName; } 
            set
            { 
                domainName = value;
                SetSslPaths();
            } 
        }
        public static string LocalServerDnsHostName { get; set; } = "manage";
        public static string RemoteServerDnsHostName { get; set; } = "watch";
        public static string? AppMasterPassword { get; set; }
        public static int WaitSecsForObsConnection { get; set; } = 10;
        public static string? OverrideSslCertPath { set { SslCertPath = overrideSslCertPath = value; } }
        public static string? OverrideSslKeyPath { set { SslKeyPath = overrideSslKeyPath = value; } } 
        public static string? OverrideSslPfxPath { set { SslPfxPath = overrideSslPfxPath = value; } }
        public static string? SslBasePath {
            get { return sslBasePath; }
            set { 
                sslBasePath = value;
                SetSslPaths();
            } 
        }
        public static string? SslCertPath { get; set; }
        public static string? SslKeyPath { get; set; } 
        public static string? SslPfxPath { get; set; } 
        public static string RemoteSetupScript { get; set; } = "output_resources/remote_setup.sh";
        public static string BroadcastAuthZip { get; set; } = "output_resources/BroadcastAuth.zip";
        public static int ShutdownDelaySeconds { get; set; } = 300;

        private static void SetSslPaths()
        {
            if ( DomainName != null && SslBasePath != null )
            {
                SslCertPath = string.IsNullOrWhiteSpace( overrideSslCertPath ) ? $"{SslBasePath.TrimEnd( '/' )}/{DomainName}.full-chain.crt" : overrideSslCertPath;
                SslKeyPath = string.IsNullOrWhiteSpace( overrideSslKeyPath ) ? $"{SslBasePath.TrimEnd( '/' )}/{DomainName}.key" : overrideSslKeyPath;
                SslPfxPath = string.IsNullOrWhiteSpace( overrideSslPfxPath ) ? $"{SslBasePath.TrimEnd( '/' )}/{DomainName}.pfx" : overrideSslPfxPath;
            }
        }
    }

    public class Logging
    {
        public static Loglevel? LogLevel { get; set; }
    }

    public class Loglevel
    {
        public static string? Default { get; set; }
        public static string? MicrosoftAspNetCore { get; set; }
    }


}

