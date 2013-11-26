using System.Web.Profile;
using RedisProviders.TestSite.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;

namespace RedisProviders.TestSite.Controllers
{
    public class ProfileController : Controller
    {
        private const string LONG_STRING =
            @"Lorem ipsum dolor sit amet, consectetur adipiscing elit. Morbi felis felis, suscipit vel fermentum dictum, egestas in odio. Sed nisl mi, mollis eget ornare et, gravida a est. Suspendisse tortor purus, gravida id varius in, tincidunt in velit. Duis condimentum libero quis mauris fringilla ut ornare ligula interdum. Vestibulum ac arcu a arcu suscipit luctus. Nullam sed tellus ullamcorper mi fermentum mollis. Duis sed nunc nec enim malesuada mollis.
            Nulla vitae velit justo, vel pretium lacus. Quisque augue leo, commodo nec commodo ut, fringilla a mi. Nullam ac justo sit amet libero tincidunt convallis. Maecenas eu ornare metus. Mauris posuere enim sed purus suscipit semper. Donec dui arcu, congue varius vestibulum at, tempor quis erat. Nullam turpis quam, consectetur vel imperdiet at, consequat et justo. Fusce sed metus at purus auctor tempus nec vel felis. Quisque ac orci dui, non rhoncus magna. Aenean ligula metus, iaculis a dictum ut, pharetra a dui.
            Praesent lectus nisi, tempus tempor suscipit eget, ullamcorper vitae tortor. Pellentesque diam dui, auctor eu porttitor vel, vehicula at orci. Cras sit amet congue est. Phasellus tempus ligula quis nibh dignissim scelerisque. Nulla ullamcorper placerat arcu, et posuere leo fringilla nec. Quisque at odio turpis, ac condimentum mauris. Phasellus ipsum massa, gravida vel aliquam ac, cursus condimentum felis. Duis volutpat rutrum purus, in vulputate elit blandit ut. Morbi fringilla pharetra venenatis. Morbi metus nulla, fermentum vel congue ut, sagittis sit amet nulla.
            Sed ultricies dignissim sollicitudin. Cras placerat tristique dapibus. Aliquam rutrum est a sem mattis laoreet. Phasellus vehicula neque sed lacus rhoncus vitae tempus neque laoreet. Etiam dolor ipsum, vulputate sed porttitor ac, condimentum vel leo. In accumsan magna eget lorem scelerisque eget sagittis justo mollis. Pellentesque habitant morbi tristique senectus et netus et malesuada fames ac turpis egestas. Nulla dolor risus, molestie quis tincidunt vitae, posuere at neque. Aliquam erat volutpat. Suspendisse fringilla rutrum enim sed ultricies. Nullam odio lectus, rhoncus in iaculis vel, iaculis ac urna. Praesent fermentum elementum leo lacinia vehicula. Aenean bibendum, augue ut semper pellentesque, nisi arcu feugiat nulla, vel hendrerit arcu risus ut dui.
            Vivamus vel mi vitae nibh fermentum ullamcorper at eget leo. Proin porta accumsan est, ut tristique nulla lacinia fringilla. Praesent sit amet urna sapien. Donec risus velit, scelerisque laoreet congue vel, molestie ac leo. Sed pretium, odio a euismod fermentum, lectus ipsum hendrerit urna, in sodales lacus tortor id arcu. Fusce libero lectus, fermentum ullamcorper tempor a, euismod nec odio. Duis rutrum lacus vel dui vestibulum a euismod massa cursus.";

        //
        // GET: /Profile/

        public ActionResult Index()
        {
            ViewBag.TestString = Profile["TestString"];
            Profile["TestString"] = LONG_STRING;

            ViewBag.CurrentDateTime = (DateTime)Profile["DateTime"];
            Profile["DateTime"] = DateTime.UtcNow;

            ViewBag.TestObject = (TestObject)Profile["TestObject"];
            

            var obj = (TestObject)Profile["TestObject"];
            ViewBag.TestObjectId = obj.Id;
            ViewBag.TestObjectName = obj.Name;
            ViewBag.TestObjectModifiedDateTime = obj.ModifiedDateTime;
            ViewBag.TestObjectCount = obj.Count;

            Profile["TestObject"] = new TestObject { Id = Guid.NewGuid(), ModifiedDateTime = DateTime.UtcNow, Name = "Testing", Count = DateTime.UtcNow.Second };

            return View();
        }

        public ActionResult Overview()
        {
            
            ViewBag.All  = ProfileManager.GetAllProfiles(ProfileAuthenticationOption.All);
            ViewBag.Anon = ProfileManager.GetAllProfiles(ProfileAuthenticationOption.Anonymous);
            ViewBag.Auth = ProfileManager.GetAllProfiles(ProfileAuthenticationOption.Authenticated);

            //ViewBag.Inactive = ProfileManager.GetAllInactiveProfiles(ProfileAuthenticationOption.All, DateTime.Now.Subtract(TimeSpan.FromMinutes(1)));
            return View();
        }

    }
}
