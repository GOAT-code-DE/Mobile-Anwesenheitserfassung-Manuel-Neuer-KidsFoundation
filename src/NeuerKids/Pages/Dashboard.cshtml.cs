using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NeuerKids.Services;

namespace NeuerKids.Pages;

public class DashboardModel(Access access) : PageModel
{
    public async Task<IActionResult> OnGetAsync()
    {
        try
        {
            await access.Sites(User, [], manager: true);
            return Page();
        }
        catch (AppError error) when (error.Status == 403)
        {
            return StatusCode(403);
        }
    }
}
