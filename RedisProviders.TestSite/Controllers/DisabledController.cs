using System;
using System.Web.Mvc;
using System.Web.SessionState;

namespace RedisProviders.TestSite.Controllers
{
    [SessionState(SessionStateBehavior.Disabled)]
    public class DisabledController : Controller
    {
        // GET: /Disabled/
        public ActionResult Index()
        {
            // If session state is disabled accessing the index on the session object will throw a null reference exception
            try
            {
                Session["test"] = "test";
            }
            catch (Exception ex)
            {
                ViewBag.WriteException = ex;
            }
            try
            {
                ViewBag.Test = Session["test"];
            }
            catch (Exception ex)
            {
                ViewBag.ReadException = ex;
            }
            return View();
        }
    }
}