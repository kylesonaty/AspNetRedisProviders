using System;
using System.Web.Mvc;

namespace RedisProviders.TestSite.Controllers
{
    public class OutputCacheController : Controller
    {
        //
        // GET: /OutputCache/
        [OutputCache(Duration=120,VaryByParam="none")]
        public ActionResult Index()
        {
            ViewBag.Now = DateTime.UtcNow;
            return View();
        }

    }
}
