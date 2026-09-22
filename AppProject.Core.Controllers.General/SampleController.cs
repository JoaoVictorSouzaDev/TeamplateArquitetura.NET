#if DEBUG
using AppProject.Resources;
using Microsoft.AspNetCore.Mvc;

namespace AppProject.Core.Controllers.General
{
    [Route("api/general/[controller]/[action]")]
    [ApiController]
    public class SampleController : ControllerBase
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
    }
}
#endif