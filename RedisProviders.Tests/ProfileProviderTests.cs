using BookSleeve;
using NUnit.Framework;
using System.Web.Profile;

namespace RedisProviders.Tests
{
    [TestFixture]
    class ProfileProviderTests
    {

        [TestFixtureSetUp]
        public void Setup()
        {
            var connection = new RedisConnection("127.0.0.1", allowAdmin: true);
            connection.Open();
            connection.Server.FlushDb(0);
            connection.Close(false);
        }

        [Test]
        public void TestDeleteProfile()
        {
            var results = ProfileManager.DeleteProfile("tester");
            Assert.False(results);
        }
    }
}