using System.Security.Claims;

namespace BroadcastManager2
{
    public class SimpleClaim : Claim
    {
        // need to be able to deserialize a Claim into a type with a single constructor instance
        // so - inherit Claim and expose just one constructor
        public SimpleClaim( string type, string value ) : base( type, value )
        {
            
        }
    }
}
