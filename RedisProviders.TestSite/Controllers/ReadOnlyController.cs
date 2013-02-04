using System.Web.Mvc;
using System.Web.SessionState;

namespace RedisProviders.TestSite.Controllers
{
    [SessionState(SessionStateBehavior.ReadOnly)]
    public class ReadOnlyController : Controller
    {
        //
        // GET: /ReadOnly/

        public ActionResult Index()
        {
            ViewBag.Time = Session["Time"];
            ViewBag.Text = Session["Text"];

            // this call will set the session object test value but it will not presist to the session state store
            Session["Test"] = "test";
            ViewBag.Test = Session["Test"];
            
            return View();
        }
    }
}