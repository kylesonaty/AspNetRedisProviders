using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace RedisProviders.TestSite.Models
{
    public class TestObject
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public DateTime ModifiedDateTime { get; set; }
        public int Count { get; set; }
    }
}