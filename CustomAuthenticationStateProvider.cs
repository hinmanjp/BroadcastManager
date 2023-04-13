
using Newtonsoft.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Vultr.API.Models;
using BroadcastManager2.Extensions;
using Microsoft.AspNetCore.Authentication;
using Renci.SshNet.Security;

namespace BroadcastManager2
{
    public class CustomAuthenticationStateProvider : AuthenticationStateProvider
    {
        [Inject] ProtectedSessionStorage? pss { get; set; }
        public CustomAuthenticationStateProvider( ProtectedSessionStorage Pss )
        {
            pss = Pss;
        }

        public override async Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            ClaimsIdentity? identity = null;

            if ( pss != null )
            {
                var result = await pss.GetAsync<string>( "auth", "claims" );
                if ( result.Success )
                {
                    var claims = JsonConvert.DeserializeObject<SimpleClaim[]>( result.Value ?? "{}" );
                    identity = new ClaimsIdentity( claims, "custom_auth" );
                    // validate that the user REALLY is authenticated and that the expriation date for the login hasn't passed.
                    // Update the expriation date if still valid.
                    if ( identity.IsAuthenticated && identity.GetExpiration() > DateTime.UtcNow )
                    {
                        var newClaims = new Claim[] { new Claim(ClaimTypes.Name, identity?.Name)
                                 , new Claim(ClaimTypes.Expiration, DateTime.UtcNow.AddMinutes(AppSettings.SessionTimeoutMinutes).ToString(), typeof(DateTime).FullName) };
                        var claimsJson = JsonConvert.SerializeObject( newClaims );
                        await pss.SetAsync( "auth", "claims", claimsJson );
                    }
                    else
                        identity = null;
                    
                }
            }
            
            //if (identity == null)
            identity ??= new ClaimsIdentity();
            var ident = (ClaimsIdentity)identity;

            var user = new ClaimsPrincipal(ident);

            
            NotifyAuthenticationStateChanged( Task.FromResult( new AuthenticationState( user ) ) );
            await Task.Delay( 0 );
            return new AuthenticationState( user );
        }

    }
}