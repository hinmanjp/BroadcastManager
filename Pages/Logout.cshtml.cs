using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BroadcastManager2.Pages
{
    public class LogoutModel : PageModel
    {
        [Inject] private CustomAuth _auth { get; set; }

        public async Task<IActionResult> OnPost()
        {
            await HttpContext.SignOutAsync();
            return Redirect( "~/" );
        }

        public void OnGet()
        {
        }
    }
}
