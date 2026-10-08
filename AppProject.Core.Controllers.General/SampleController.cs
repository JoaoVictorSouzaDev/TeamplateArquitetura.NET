#if DEBUG
using AppProject.Core.Contracs;
using AppProject.Resources;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AppProject.Core.Controllers.General
{
    [Route("api/general/[controller]/[action]")]
    [ApiController]
    public class SampleController(
        IUserContext userContext)
        : ControllerBase
    {
        [HttpGet]
        public IActionResult GetSample()
        {
            return this.Ok("This is sample response from GeneralSampleResponse.");
        }

        [HttpGet]
        public IActionResult GetCultureSample()
        {
            return this.Ok(StringResource.GetStringByKey("Sample_Message_Text"));
        }

        [Authorize]
        [HttpGet]
        public IActionResult GetProtectedData()
        {
            return this.Ok("This is a protected data!");
        }

        [Authorize]
        [HttpGet]
        public async Task<IActionResult> GetCurrentUserEmailAsync(CancellationToken cancellationToken)
        {
            var currentUser = await userContext.GetCurrentUserAsync(cancellationToken);
            var systemAdminUser = await userContext.GetSystemAdminUserAsync(cancellationToken);

            var message = $"Current user email: {currentUser.Email}. " + $"System admin user email: {systemAdminUser.Email}";

            return this.Ok(message);
        }
    }
}
#endif