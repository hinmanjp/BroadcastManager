using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace BroadcastManager2.Pages
{
    public class LoginModel : PageModel
    {
  
        private CustomAuth _auth;

        [Required]
        [StringLength( 20, ErrorMessage = "Password is too long." )]
        public string? Password { get; set; }

        public LoginModel( CustomAuth auth )
        { 
            _auth = auth;
            Password = "";
        }

        public async Task<IActionResult> OnPostAsync()
        {
            //ReturnUrl = Url.Content( "~/manager" );
            if (ModelState.IsValid)
            {
                if ( IsValid( Password ?? "" ) )
                {
                    var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "Broadcast Manager"), }, "Custom_Auth");
                    var user = new ClaimsPrincipal(identity);
                    await HttpContext.SignInAsync( CookieAuthenticationDefaults.AuthenticationScheme, user );
                    return LocalRedirect( Url.Content( "/manager" ) );
                }
            }
            return Page();
        }


        private bool IsValid( string authCode )
        {
            string AppMasterPassword = "";
            if ( AppSettings.Config != null )
                AppMasterPassword = AppSettings.Config["AppMasterPassword"] ?? "";

            if ( !string.IsNullOrWhiteSpace( AppMasterPassword ) && authCode == AppMasterPassword )
                return true;
            else
            {
                // check against auth table in db
            }
            return false;
        }

        public void OnGet()
        {
        }


    }
}
