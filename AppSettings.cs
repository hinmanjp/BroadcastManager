namespace BroadcastManager2
{
    public class AppSettings 
    {
        private static IConfiguration? _config;
        public static IConfiguration Config {
            get { return _config ?? new ConfigurationBuilder().Build(); }
            internal set { _config = value; }
        }
        public AppSettings(IConfiguration configuration) 
        {
            _config = configuration;
        }
    }
}
