using System.Security.Claims;
using System.Security.Principal;

namespace BroadcastManager2.Extensions
{
    public static class IdentityExtensions
    {
        public static DateTime GetExpiration( this IIdentity? identity )
        {
            ClaimsIdentity claimsIdentity = identity as ClaimsIdentity;
            Claim claim = claimsIdentity?.FindFirst(ClaimTypes.Expiration);

            if ( claim == null )
                return DateTime.Now.AddMinutes(-1);

            return DateTime.Parse( claim.Value );
        }
    }
}
