using System.Security.Claims;

namespace BroadcastManager2
{
    public class CustomAuth
    {
        public Dictionary<string, ClaimsPrincipal> Users { get; set; }
        public CustomAuth() 
        { 
            Users = new Dictionary<string, ClaimsPrincipal>();
        }
    }
}
