﻿namespace BroadcastManager2
{
    public class AppSettings 
    {
        public static IConfiguration? Config { get; set; }
        public AppSettings(IConfiguration configuration) 
        {
            Config = configuration;
        }
    }
}
