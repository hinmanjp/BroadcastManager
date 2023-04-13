using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Text.RegularExpressions;
using static MudBlazor.CategoryTypes;

namespace BroadcastManager2
{
    public class AppSettings
    {
        IConfiguration configuration;

        private static string? domainName;
        private static string? sshPrivateKey;
        private static string? sshPublicKey;
        private static string sslBasePath = "/etc/ssl/";
        private static string? vultrVmLabel;

        public AppSettings(IConfiguration Configuration) 
        { 
            this.configuration = Configuration;
        }

        public AppSettings()
        { }

        public static string AppDir
        {
            get
            {
                ProcessModule? mainModule = Process.GetCurrentProcess().MainModule;
                return Path.GetDirectoryName( mainModule?.FileName ) ?? "";
            }
        }
        public static Logging? Logging { get; set; }
        public static string AllowedHosts { get; set; } = "*";
        public static string? VultrApiKey { get; set; }
        public static string VultrUrl { get; } = "https://api.vultr.com/v2/";

        // replace underscores with dashes. API throws an exception on underscores.
        public static string? VultrVmLabel { get { return vultrVmLabel; } set { vultrVmLabel = value?.Replace( '_', '-' ); } }
        public static string? SshPrivateKeyFile
        { 
            get{ return sshPrivateKey; }
        
            set 
            {
                if (  value != null && !Path.IsPathRooted( value ) )
                    sshPrivateKey = Path.Combine( AppDir, value);
            }
        }
        public static string? SshPublicKeyFile
        {
            get { return sshPublicKey; }

            set
            {
                if ( value != null && !Path.IsPathRooted( value ) )
                    sshPublicKey = Path.Combine( AppDir, value );
            }
        }
        public static string? ObsApiKey { get; set; }
        public static string? OverrideObsUrl { get { return ObsUrl; } set { ObsUrl = string.IsNullOrWhiteSpace(value) ? ObsUrl : value; } }
        public static string ObsUrl { get; set; } = "ws://127.0.0.1:4455";
        public static string? CloudFlareTokenKey { get; set; }
        public static string? DomainName { 
            get { return domainName; } 
            set
            { 
                domainName = value?.ToLower();
                if ( value!= null && string.IsNullOrWhiteSpace( VultrVmLabel ) )
                    VultrVmLabel = Regex.Match( value.ToLower(), "(^[^.]*)" ).Value + "-broadcast"; 

                SetSslPaths();
            } 
        }
        public static string LocalServerDnsHostName { get; set; } = "manage";
        public static string RemoteServerDnsHostName { get; set; } = "watch";
        public static string? AppMasterPassword { get; set; }
        public static int WaitSecsForObsConnection { get; set; } = 10;
        public static string SslBasePath 
        { 
            get { return sslBasePath; } 
            set { sslBasePath = value; SetSslPaths(); } 
        }
        public static string? SslCertPath { get; private set; }
        public static string? SslKeyPath { get; private set; } 
        public static string? SslPfxPath { get; private set; } 

        public static string RemoteSetupScript { get; set; } = "output_resources/remote_setup.sh";
        public static string BroadcastAuthZip { get; set; } = "output_resources/BroadcastAuth.zip";
        public static int ShutdownDelaySeconds { get; set; } = 300;
        public static string? TimeZone { get; set; }

        public static Vultr.Models.Instance? RemoteVM { get; set; }

        private static void SetSslPaths()
        {
            if ( DomainName != null )
            {
                SslCertPath = $"{sslBasePath.TrimEnd( '/' )}/{DomainName}.chain.crt";
                SslKeyPath = $"{sslBasePath.TrimEnd( '/' )}/{DomainName}.key";
                SslPfxPath = $"{sslBasePath.TrimEnd( '/' )}/{DomainName}.pfx";
            }
        }

        private static bool IsNullable<T>( T value )
        {
            return Nullable.GetUnderlyingType( typeof( T ) ) != null;
        }

        public static ValidationResponse ValidateAppSettings()
        {
            var response = new ValidationResponse(true, "");

            Type type = typeof(AppSettings); // static class with static properties
            foreach ( var pi in type.GetProperties( ) )
            {
                if ( IsNullable( pi.GetType() ) ) // this doesn't seem to see anything as actually being nullable :(
                {
                    if ( pi.GetValue( null, null ) == null )
                    {
                        response.IsValid = false;
                        response.Message += $"{pi.Name} is required, but has not been set.<br/>";
                    }
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

