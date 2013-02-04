using System;
using System.Web.Security;
using BookSleeve;
using NUnit.Framework;

namespace RedisProviders.Tests
{
    [TestFixture]
    public class MembershipProviderTests
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
        public void TestCreate()
        {
            var user = Membership.CreateUser("user", "password");
            Assert.AreEqual("user", user.UserName);
        }

        [Test]
        public void TestCreateWithEmail()
        {
            var user = Membership.CreateUser("emailuser", "password", "email@user.com");
            Assert.AreEqual("emailuser", user.UserName);
            Assert.AreEqual("email@user.com", user.Email);
        }

        [Test]
        public void TestCreateCaseInsensative()
        {
            Membership.CreateUser("abc", "pass");
            Assert.Throws<MembershipCreateUserException>( () => Membership.CreateUser("ABC", "pass"));
        }

        [Test]
        public void TestCreateWithPasswordQuestionAndAnswer()
        {
            MembershipCreateStatus status;
            var user = Membership.CreateUser("u", "p", "e", "q", "a", true, out status);
            Assert.AreEqual(MembershipCreateStatus.Success, status);
            Assert.IsNotNull(user);
            Assert.AreEqual("u", user.UserName);
            Assert.AreEqual("q", user.PasswordQuestion);
            Assert.AreEqual("e", user.Email);
        }

        [Test]
        public void TestCreateWithPasswordQuestionAndAnswerWithProviderKey()
        {
            MembershipCreateStatus status;
            var key = Guid.NewGuid();
            var user = Membership.CreateUser("u1", "p", "e1", "q", "a", true, key, out status);
            Assert.AreEqual(MembershipCreateStatus.Success, status);
            Assert.IsNotNull(user);
            Assert.AreEqual("u1", user.UserName);
            Assert.AreEqual("q", user.PasswordQuestion);
            Assert.AreEqual("e1", user.Email);
            Assert.AreEqual(key, user.ProviderUserKey);
        }

        [Test]
        public void TestDuplicateUser()
        {
            Membership.CreateUser("duplicateuser", "password");
            Assert.Throws(typeof(MembershipCreateUserException), () => Membership.CreateUser("duplicateuser", "password"));
        }

        [Test]
        public void TestDuplicateEmail()
        {
            Membership.CreateUser("duplicateemail1", "password", "email");
            Assert.Throws(typeof(MembershipCreateUserException), () => Membership.CreateUser("duplicateemail2", "password", "email"));
        }

        [Test]
        public void TestValidateUser()
        {
            Membership.CreateUser("validateUser", "password", "validateUser@find.me");
            var result = Membership.ValidateUser("validateUser", "password");
            Assert.True(result);
            var resultFail = Membership.ValidateUser("validateUser", "password2");
            Assert.False(resultFail);
        }

        [Test]
        public void TestUpdateUser()
        {
            var user = Membership.CreateUser("updateUser", "password", "updateUser@find.me");
            user.Comment = "new comments";
            Membership.UpdateUser(user);
            var userAfterUpdate = Membership.GetUser(user.UserName);
            Assert.IsNotNull(userAfterUpdate);
            Assert.AreEqual("new comments", userAfterUpdate.Comment);
        }

        [Test]
        public void TestGetUserByProviderKey()
        {
            var user = Membership.CreateUser("keyUser", "password", "keyUser@find.me");
            Assert.IsNotNull(user.ProviderUserKey);
            var userByKey = Membership.GetUser(user.ProviderUserKey);
            Assert.IsNotNull(userByKey);
            Assert.AreEqual("keyUser", userByKey.UserName);
        }

        [Test]
        public void TestGetUserNameByEmail()
        {
            Membership.CreateUser("userbyemail", "password", "userbyemail@find.me");
            var username = Membership.GetUserNameByEmail("userbyemail@find.me");
            Assert.AreEqual("userbyemail", username);
        }

        [Test]
        public void TestGetAllUsers()
        {
            var users = Membership.GetAllUsers();
            Assert.Greater(users.Count, 0);
        }

        [Test]
        public void TestGetAllUsersPaged()
        {
            int totalRecords;
            var users = Membership.GetAllUsers(0, 10, out totalRecords);
            Assert.Greater(users.Count, 0);
        }

        [Test]
        public void TestGetNumberOfUsersOnline()
        {          
            Membership.CreateUser("user1", "password");
            Membership.CreateUser("user2", "password");
            Membership.CreateUser("user3", "password");

            Membership.GetUser("user1", true);
            Membership.GetUser("user2", true);
            Membership.GetUser("user3", true);

            var usersOnline = Membership.GetNumberOfUsersOnline();
            Assert.Greater(usersOnline, 0);
            Assert.AreEqual(3, usersOnline);
        }        
    }
}
