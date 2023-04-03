
using Newtonsoft.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using System;
using System.Security.Claims;
using System.Threading.Tasks;

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
                }
            }
            
            //if (identity == null)
            identity ??= new ClaimsIdentity();
            var user = new ClaimsPrincipal(identity);

            NotifyAuthenticationStateChanged( Task.FromResult( new AuthenticationState( user ) ) );
            await Task.Delay( 0 );
            return new AuthenticationState( user );
        }

    }
}