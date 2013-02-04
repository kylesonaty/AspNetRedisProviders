using System;
using System.Collections.Specialized;
using System.Configuration.Provider;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Web.Hosting;
using System.Web.Security;
using BookSleeve;

namespace RedisProviders
{
    public class RedisRoleProvider : RoleProvider
    {
        private const string ROLES_KEY = "application:{0}:roles";
        private const string ROLE_USER_KEY = "application:{0}:role:{1}:users";
        private const string USER_ROLE_KEY = "application:{0}:user:{1}:roles";
        private const string REDIS_CONNECTION_FAILED = "Redis connection failed.";
        private const string EXCEPTION_MESSAGE = "An exception occurred. Please check the Event Log.";

        private const string EVENT_SOURCE = "RedisRoleProvider";
        private const string EVENT_LOG = "Application";

        private int _redisDb;
        private string _host;
        private int _port;
        private string _password;
        private bool _writeExceptionsToEventLog;

        private static RedisConnection _connection;

        public override string ApplicationName { get; set; }

        public override void Initialize(string name, NameValueCollection config)
        {
            if (config == null)
                throw new ArgumentNullException("config");

            if (string.IsNullOrEmpty(name))
                name = "RedisRoleProvider";

            _host = string.IsNullOrEmpty(config["host"]) ? "localhost" : config["host"];
            _port = string.IsNullOrEmpty(config["port"]) ? 6379 : int.Parse(config["port"]);
            _redisDb = string.IsNullOrEmpty(config["db"]) ? 0 : int.Parse(config["db"]);
            _password = !string.IsNullOrEmpty(config["password"]) ? null : config["password"];

            base.Initialize(name, config);

            ApplicationName = String.IsNullOrEmpty(config["applicationName"]) ? HostingEnvironment.ApplicationVirtualPath : config["applicationName"];

            if (config["writeExceptionsToEventLog"] != null)
            {
                if (config["writeExceptionsToEventLog"].ToUpper() == "TRUE")
                {
                    _writeExceptionsToEventLog = true;
                }
            }
        }

        public override bool IsUserInRole(string username, string roleName)
        {
            try
            {
                var connection = GetConnection();
                var task = connection.Sets.Contains(_redisDb, GetRoleUsersKey(roleName), username);
                var result = connection.Wait(task);
                return result;
            }
            catch (Exception ex)
            {
                if (_writeExceptionsToEventLog)
                {
                    WriteToEventLog(ex, "IsUserInRole");
                    throw new ProviderException(EXCEPTION_MESSAGE);
                }
                throw;
            }
        }

        public override string[] GetRolesForUser(string username)
        {
            try
            {
                var connection = GetConnection();
                var task = connection.Sets.GetAllString(_redisDb, GetUserRolesKey(username));
                var result = connection.Wait(task);
                return result;
            }
            catch (Exception ex)
            {
                if (_writeExceptionsToEventLog)
                {
                    WriteToEventLog(ex, "GetRolesForUser");
                    throw new ProviderException(EXCEPTION_MESSAGE);
                }
                throw;
            }
        }

        public override void CreateRole(string roleName)
        {
            try
            {
                var connection = GetConnection();
                connection.Sets.Add(_redisDb, GetRolesKey(), roleName);
            }
            catch (Exception ex)
            {
                if (_writeExceptionsToEventLog)
                {
                    WriteToEventLog(ex, "CreateRole");
                    throw new ProviderException(EXCEPTION_MESSAGE);
                }
                throw;
            }
        }

        public override bool DeleteRole(string roleName, bool throwOnPopulatedRole)
        {
            try
            {
                var connection = GetConnection();
                if (throwOnPopulatedRole)
                {
                    var usersInRoleCountTask = connection.Sets.GetLength(_redisDb, GetRoleUsersKey(roleName));
                    var usersInRoleCount = connection.Wait(usersInRoleCountTask);
                    if (usersInRoleCount > 0)
                    {
                        throw new ProviderException("Cannot delete a populated role.");
                    }
                }

                var usersInRoleTask = connection.Sets.GetAllString(_redisDb, GetRoleUsersKey(roleName));
                var usersInRole = connection.Wait(usersInRoleTask);
                foreach (var user in usersInRole)
                {
                    connection.Sets.Remove(_redisDb, GetUserRolesKey(user), roleName);
                }

                connection.Sets.Remove(_redisDb, GetRolesKey(), roleName);
                connection.Keys.Remove(_redisDb, GetRoleUsersKey(roleName));
                
                return true;
            }
            catch (Exception ex)
            {
                if (_writeExceptionsToEventLog)
                {
                    WriteToEventLog(ex, "DeleteRole");
                    throw new ProviderException(EXCEPTION_MESSAGE);
                }
                throw;
            }
        }

        public override bool RoleExists(string roleName)
        {
            try
            {
                var connection = GetConnection();
                var task = connection.Sets.Contains(_redisDb, GetRolesKey(), roleName);
                var result = connection.Wait(task);
                return result;
            }
            catch (Exception ex)
            {
                if (_writeExceptionsToEventLog)
                {
                    WriteToEventLog(ex, "RoleExists");
                    throw new ProviderException(EXCEPTION_MESSAGE);
                }
                throw;
            }
        }

        public override void AddUsersToRoles(string[] usernames, string[] roleNames)
        {
            try
            {
                var connection = GetConnection();
                foreach (var roleName in roleNames)
                {
                    foreach (var username in usernames)
                    {
                        connection.Sets.Add(_redisDb, GetRoleUsersKey(roleName), username);
                        connection.Sets.Add(_redisDb, GetUserRolesKey(username), roleName);
                    }
                }
            }
            catch (Exception ex)
            {
                if (_writeExceptionsToEventLog)
                {
                    WriteToEventLog(ex, "AddUsersToRoles");
                    throw new ProviderException(EXCEPTION_MESSAGE);
                }
                throw;
            }
        }

        public override void RemoveUsersFromRoles(string[] usernames, string[] roleNames)
        {
            try
            {
                var connection = GetConnection();
                foreach (var roleName in roleNames)
                {
                    foreach (var username in usernames)
                    {
                        connection.Sets.Remove(_redisDb, GetRoleUsersKey(roleName), username);
                        connection.Sets.Remove(_redisDb, GetUserRolesKey(username), roleName);
                    }
                }
            }
            catch (Exception ex)
            {
                if (_writeExceptionsToEventLog)
                {
                    WriteToEventLog(ex, "RemoveUsersFromRoles");
                    throw new ProviderException(EXCEPTION_MESSAGE);
                }
                throw;
            }
        }

        public override string[] GetUsersInRole(string roleName)
        {
            try
            {
                var connection = GetConnection();
                var task = connection.Sets.GetAllString(_redisDb, GetRoleUsersKey(roleName));
                var result = connection.Wait(task);
                return result;
            }
            catch (Exception ex)
            {
                if (_writeExceptionsToEventLog)
                {
                    WriteToEventLog(ex, "GetUsersInRole");
                    throw new ProviderException(EXCEPTION_MESSAGE);
                }
                throw;
            }
        }

        public override string[] GetAllRoles()
        {
            try
            {
                var connection = GetConnection();
                var task = connection.Sets.GetAllString(_redisDb, GetRolesKey());
                var result = connection.Wait(task);
                return result;
            }
            catch (Exception ex)
            {
                if (_writeExceptionsToEventLog)
                {
                    WriteToEventLog(ex, "GetAllRoles");
                    throw new ProviderException(EXCEPTION_MESSAGE);
                }
                throw;
            }
        }

        public override string[] FindUsersInRole(string roleName, string usernameToMatch)
        {
            try
            {
                var connection = GetConnection();
                var task = connection.Sets.GetAllString(_redisDb, GetRoleUsersKey(roleName));
                var usernames = connection.Wait(task);
                var matches = usernames.Where(x => x.Contains(usernameToMatch));
                return matches.ToArray();
            }
            catch (Exception ex)
            {
                if (_writeExceptionsToEventLog)
                {
                    WriteToEventLog(ex, "FindUsersInRole");
                    throw new ProviderException(EXCEPTION_MESSAGE);
                }
                throw;
            }
        }

        private static void WriteToEventLog(Exception ex, string action)
        {
            var log = new EventLog { Source = EVENT_SOURCE, Log = EVENT_LOG };
            var message = "Action: " + action + "\n\n";
            message += "Exception: " + ex;
            log.WriteEntry(message);
        }

        private string GetRolesKey()
        {
            return string.Format(ROLES_KEY, ApplicationName);
        }

        private string GetRoleUsersKey(string role)
        {
            return string.Format(ROLE_USER_KEY, ApplicationName, role);
        }

        private string GetUserRolesKey(string user)
        {
            return string.Format(USER_ROLE_KEY, ApplicationName, user);
        }

        public RedisConnection GetConnection()
        {
            if (_connection == null)
            {
                _connection = new RedisConnection(_host, _port, password: _password);
                _connection.Error += OnConnectionError;
            }

            if (_connection.State == RedisConnectionBase.ConnectionState.Opening)
                return _connection;

            if (_connection.State == RedisConnectionBase.ConnectionState.Closing || _connection.State == RedisConnectionBase.ConnectionState.Closed)
            {
                try
                {
                    _connection = new RedisConnection(_host, _port, password: _password);
                    _connection.Error += OnConnectionError;
                }
                catch (Exception ex)
                {
                    throw new Exception(REDIS_CONNECTION_FAILED, ex);
                }
            }

            if (_connection.State == RedisConnectionBase.ConnectionState.Shiny)
            {
                try
                {
                    var openTask = _connection.Open();
                    _connection.Wait(openTask);
                }
                catch (SocketException ex)
                {
                    throw new Exception(REDIS_CONNECTION_FAILED, ex);
                }
            }
            return _connection;
        }

        private void OnConnectionError(object sender, ErrorEventArgs args)
        {
            if (_writeExceptionsToEventLog)
            {
                WriteToEventLog(args.Exception, string.Format("Sender: {0}\n\nCause: {1}", sender, args.Cause));
                throw new ProviderException(EXCEPTION_MESSAGE);
            }
        }
    }
}