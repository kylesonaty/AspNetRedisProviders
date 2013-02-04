using System.Linq;
using System.Web.Security;
using BookSleeve;
using NUnit.Framework;

namespace RedisProviders.Tests
{
    [TestFixture]
    public class RoleProviderTests
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
        public void TestAdd()
        {
            const string s = "role1";
            Roles.CreateRole(s);
            var roles = Roles.GetAllRoles();
            Assert.Contains(s, roles);
        }

        [Test]
        public void TestDelete()
        {
            const string s = "role2";
            Roles.CreateRole(s);
            var rolesBefore = Roles.GetAllRoles();
            Assert.Contains(s, rolesBefore);
            Roles.DeleteRole(s);
            var rolesAfter = Roles.GetAllRoles();
            Assert.False(rolesAfter.Contains(s));
        }

        [Test]
        public void TestExists()
        {
            const string s = "role3";
            Roles.CreateRole(s);
            Assert.True(Roles.RoleExists(s));
        }

        [Test]
        public void TestAddUserToRole()
        {
            const string role = "role4";
            const string user1 = "user1";
            const string user2 = "user2";
            Roles.CreateRole(role);
            Roles.AddUserToRole(user1, role);
            Roles.AddUserToRole(user2, role);
            var roles = Roles.GetUsersInRole(role);
            Assert.Contains(user1, roles);
            Assert.Contains(user2, roles);

            Assert.True(Roles.IsUserInRole(user1, role));
            Assert.True(Roles.IsUserInRole(user2, role));
        }

        [Test]
        public void TestAddUsersToRole()
        {
            const string role = "role5";
            const string user1 = "user1";
            const string user2 = "user2";
            Roles.CreateRole(role);
            Roles.AddUsersToRole(new [] { user1, user2}, role);
            var users = Roles.GetUsersInRole(role);
            Assert.Contains(user1, users);
            Assert.Contains(user2, users);

            Assert.True(Roles.IsUserInRole(user1, role));
            Assert.True(Roles.IsUserInRole(user2, role));
        }

        [Test]
        public void TestAddUserToRoles()
        {
            const string role1 = "role6";
            const string role2 = "role7";
            const string user = "user";
            Roles.CreateRole(role1);
            Roles.CreateRole(role2);
            Roles.AddUserToRoles(user, new[] {role1, role2});

            var roles = Roles.GetRolesForUser(user);
            Assert.Contains(role1, roles);
            Assert.Contains(role1, roles);

            Assert.True(Roles.IsUserInRole(user, role1));
            Assert.True(Roles.IsUserInRole(user, role2));
        }

        [Test]
        public void TestDeleteUserFromRole()
        {
            const string role1 = "role8";
            const string role2 = "role9";
            const string user = "user";
            Roles.CreateRole(role1);
            Roles.CreateRole(role2);
            Roles.AddUserToRoles(user, new[] { role1, role2 });

            var roles = Roles.GetRolesForUser(user);
            Assert.Contains(role1, roles);
            Assert.Contains(role1, roles);

            Assert.True(Roles.IsUserInRole(user, role1));
            Assert.True(Roles.IsUserInRole(user, role2));

            Roles.RemoveUserFromRole(user, role1);
            Roles.RemoveUserFromRole(user, role2);

            Assert.False(Roles.IsUserInRole(user, role1));
            Assert.False(Roles.IsUserInRole(user, role2));
        }
    }
}
