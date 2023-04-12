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
        public AppSettings(IConfiguration Configuration) 
        { 
            this.configuration = Configuration;
        }

        private static string? sshPrivateKey;
        private static string? sshPublicKey;
        public static string AppDir 
        { 
            get
            {
                ProcessModule? mainModule = Process.GetCurrentProcess().MainModule;
                return Path.GetDirectoryName( mainModule?.FileName ) ?? "";
            }
        }

        public AppSettings()
        { }

        private static string? domainName;
        //private static string sslCertPath = "";
        //private static string sslKeyPath = "";
        //private static string sslPfxPath = "";
        //private static string? overrideSslCertPath;
        //private static string? overrideSslKeyPath;
        //private static string? overrideSslPfxPath;

        private static string? sslBasePath;

        public static Logging? Logging { get; set; }
        public static string AllowedHosts { get; set; } = "*";
        public static string? VultrApiKey { get; set; }
        public static string? VultrVmLabel { get; set; }
        public static string RemoteServerBaseName { get; set; } = "Broadcaster";
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
        //public static string? AdminUser { get; set; }
        //public static string? AdminPW { get; set; }
        public static string? CloudFlareTokenKey { get; set; }
        public static string? DomainName { 
            get { return domainName; } 
            set
            { 
                domainName = value.ToLower();
                if ( value!= null && string.IsNullOrWhiteSpace( VultrVmLabel ) )
                    VultrVmLabel = Regex.Match(value.ToLower(), "(^[^.]*)" ).Value + "_broadcast"; 

                SetSslPaths();
            } 
        }
        public static string LocalServerDnsHostName { get; set; } = "manage";
        public static string RemoteServerDnsHostName { get; set; } = "watch";
        public static string? AppMasterPassword { get; set; }
        public static int WaitSecsForObsConnection { get; set; } = 10;
        //public static string? OverrideSslCertPath { set { SslCertPath = overrideSslCertPath = value; } }
        //public static string? OverrideSslKeyPath { set { SslKeyPath = overrideSslKeyPath = value; } } 
        //public static string? OverrideSslPfxPath { set { SslPfxPath = overrideSslPfxPath = value; } }
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
                if ( !File.Exists( SslCertPath ) && !Path.IsPathRooted( SslCertPath ) )
                    SslCertPath = $"{SslBasePath.TrimEnd( '/' )}/{DomainName}.full-chain.crt";
                if ( !File.Exists( SslKeyPath ) && !Path.IsPathRooted( SslKeyPath ) )
                    SslKeyPath = $"{SslBasePath.TrimEnd( '/' )}/{DomainName}.key";
                if ( !File.Exists( SslPfxPath ) && !Path.IsPathRooted( SslPfxPath ) )
                    SslPfxPath = $"{SslBasePath.TrimEnd( '/' )}/{DomainName}.pfx";
            }
        }


        private static bool IsNullable<T>( T value )
        {
            return Nullable.GetUnderlyingType( typeof( T ) ) != null;
        }

        public static ValidationResponse ValidateAppSettings()
        {
            var response = new ValidationResponse(true, "");
            //var appSettings = new AppSettings();
            //configuration.Bind( appSettings );
            //Type type = typeof(AppSettings); // static class with static properties
            //foreach ( var p in type.GetProperties( BindingFlags.Public | BindingFlags.Instance ) )
            //{
            //    if ( p. IsNullable( p.GetType() ) )
            //    { 
                
            //    }
            //    var v = p.GetValue(null, null); // static classes cannot be instanced, so use null...
            //}

            Type type = typeof(AppSettings); // static class with static properties
            foreach ( var pi in type.GetProperties( ) )
            {
                if ( IsNullable( pi.GetType() ) )
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

