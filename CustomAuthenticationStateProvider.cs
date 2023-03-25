namespace BroadcastManager2
{
    using System;
    using System.Security.Claims;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Components.Authorization;


    public class CustomAuthenticationStateProvider : AuthenticationStateProvider
    {
        //private readonly IConfiguration? _configuration;

        public override Task<AuthenticationState> GetAuthenticationStateAsync()
        {

            var identity = new ClaimsIdentity();
            var user = new ClaimsPrincipal(identity);

            return Task.FromResult(new AuthenticationState(user));
        }

        public bool AuthenticateUser(string authCode)
        {
            if(IsValid(authCode))
            { 
                var identity = new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.Name, authCode),
                }, "Custom_Auth");

                var user = new ClaimsPrincipal(identity);

                NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(user)));
                return true;
            }
            return false;
        }

        private bool IsValid(string authCode)
        {
            string? AppMasterPassword = AppSettings.Config["AppMasterPassword"];
            if (!string.IsNullOrWhiteSpace(AppMasterPassword) && authCode == AppMasterPassword)
                return true;
            else
            {
                // check against auth table in db
            }
            return false;
        }
    }
}
