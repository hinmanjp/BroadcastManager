using System.Net.NetworkInformation;

namespace BroadcastManager2
{
    public class AppSettings
    {
        private static IConfiguration? _config;
        public static IConfiguration Config
        {
            get { return _config ?? new ConfigurationBuilder().Build(); }
            internal set { _config = value; }
        }
        public AppSettings( IConfiguration configuration )
        {
            _config = configuration;
        }

        private static string? sslCertPath;
        private static string? sslKeyPath;
        private static string? sslPfxPath;
        private static string? masterPass;

        public static Logging? Logging { get; set; }
        public static string AllowedHosts { get; set; } = "*";
        public static string? VultrApiKey { get; set; }
        public static string? VultrVmLabel { get; set; }
        public static string RemoteServerBaseName { get; set; } = "Broadcaster";
        public static string? SshPrivateKeyFile { get; set; }
        public static string? SshPublicKeyFile { get; set; }
        public static string? ObsApiKey { get; set; }
        public static string? OverrideObsUrl { get; set; }
        public static string ObsUrl { get; set; } = OverrideObsUrl ?? "ws://127.0.0.1:4455";
        //public static string? AdminUser { get; set; }
        //public static string? AdminPW { get; set; }
        public static string? CloudFlareTokenKey { get; set; }
        public static string? DomainName { get; set; }
        public static string LocalServerDnsHostName { get; set; } = "manage";
        public static string RemoteServerDnsHostName { get; set; } = "watch";
        public static string? AppMasterPassword { get { return masterPass; } set { masterPass = string.IsNullOrWhiteSpace( value ) ? null : value; } } 
        public static int WaitSecsForObsConnection { get; set; } = 10;
        public static string? OverrideSslCertPath { get { return sslCertPath; } set { sslCertPath = string.IsNullOrWhiteSpace( value ) ? null : value; } } 
        public static string? OverrideSslKeyPath { get { return sslKeyPath; } set { sslKeyPath = string.IsNullOrWhiteSpace( value ) ? null : value; } }
        public static string? OverrideSslPfxPath { get { return sslPfxPath; } set { sslPfxPath = string.IsNullOrWhiteSpace( value ) ? null : value; } }
        public static string SslBasePath { get; set; } = "/etc/ssl";
        public static string SslCertPath { get; set; } = OverrideSslCertPath ?? $"{SslBasePath.TrimEnd('/')}/{DomainName}.full-chain.crt";
        public static string SslKeyPath { get; set; } = OverrideSslKeyPath ?? $"{SslBasePath.TrimEnd( '/' )}/{DomainName}.key";
        public static string SslPfxPath { get; set; } = OverrideSslPfxPath ?? $"{SslBasePath.TrimEnd( '/' )}/{DomainName}.pfx";
        public static string RemoteSetupScript { get; set; } = "output_resources/remote_setup.sh";
        public static string BroadcastAuthZip { get; set; } = "output_resources/BroadcastAuth.zip";
        public static int ShutdownDelaySeconds { get; set; } = 300;
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

