using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Web.Hosting;
using System.Web.Profile;
using BookSleeve;
using System.Configuration.Provider;
using System.Threading.Tasks;

namespace RedisProviders
{
    public class RedisProfileProvider : ProfileProvider
    {
        private const string REDIS_CONNECTION_FAILED = "Redis connection failed.";
        private const string REDIS_PROFILE_PROVIDER = "Redis Profile Provider";
        private const string EXCEPTION_MESSAGE = "An exception occurred. Please check the Event Log.";
        private const string PROVIDER_DEFAULT_NAME = "RedisProfileProvider";

        private static RedisConnection _connection;

        private string _applicationName;
        private int _redisDb;
        private string _host;
        private int _port;
        private string _password;
        private bool _writeExceptionsToEventLog;

        public override string ApplicationName { get { return _applicationName; } set { _applicationName = value; } }

        public override void Initialize(string name, System.Collections.Specialized.NameValueCollection config)
        {
            if (config == null)
                throw new ArgumentNullException("config");

            if (string.IsNullOrEmpty(name))
                name = PROVIDER_DEFAULT_NAME;

            config.Remove("description");
            config.Add("description", REDIS_PROFILE_PROVIDER);

            // Initialize the abstract base class
            base.Initialize(name, config);

            _host = GetConfigValue(config["host"], Defaults.Host);
            _port = Convert.ToInt32(GetConfigValue(config["port"], Defaults.Port));
            _password = GetConfigValue(config["password"], null);
            _redisDb = Convert.ToInt32(GetConfigValue(config["db"], Defaults.Db));
            _applicationName = GetConfigValue(config["applicationName"], HostingEnvironment.ApplicationVirtualPath);
            _writeExceptionsToEventLog = Convert.ToBoolean(GetConfigValue(config["writeExceptionsToEventLog"], "true"));
        }

        private static string GetConfigValue(string configValue, string defaultValue)
        {
            return string.IsNullOrEmpty(configValue) ? defaultValue : configValue;
        }

        public override SettingsPropertyValueCollection GetPropertyValues(SettingsContext context, SettingsPropertyCollection collection)
        {
            throw new NotImplementedException();
        }

        public override void SetPropertyValues(SettingsContext context, SettingsPropertyValueCollection collection)
        {
            throw new NotImplementedException();
        }

        public override int DeleteProfiles(ProfileInfoCollection profiles)
        {
            throw new NotImplementedException();
        }

        public override int DeleteProfiles(string[] usernames)
        {
            throw new NotImplementedException();
        }

        public override int DeleteInactiveProfiles(ProfileAuthenticationOption authenticationOption, DateTime userInactiveSinceDate)
        {
            throw new NotImplementedException();
        }

        public override ProfileInfoCollection GetAllProfiles(ProfileAuthenticationOption authenticationOption, int pageIndex, int pageSize, out int totalRecords)
        {
            try
            {
                var connection = GetConnection();
                var start = pageIndex * pageSize;
                var end = pageIndex * pageSize + pageSize;
                string key = string.Empty;
                switch (authenticationOption)
                {
                    case ProfileAuthenticationOption.All:
                        key = GetProfilesKey();
                        break;
                    case ProfileAuthenticationOption.Anonymous:
                        key = GetProfilesKeyAnonymous();
                        break;
                    case ProfileAuthenticationOption.Authenticated:
                        key = GetProfilesKeyAuthenticated();
                        break;
                    default:
                        key = GetProfilesKey();
                        break;
                }
                var profilesTask = connection.Lists.Range(_redisDb, key, start, end);
                var profileCountTask = connection.Strings.GetString(_redisDb, GetProfilesCountKey());
                var collection = new ProfileInfoCollection();
                var profileNames = connection.Wait(profilesTask);

                Parallel.ForEach(profileNames, result =>
                {
                    var profileName = new string(Encoding.Unicode.GetChars(result));
                    var profileTask = connection.Hashes.GetAll(_redisDb, GetProfileKey(profileName));
                    var profileDict = connection.Wait(profileTask);
                    if (profileDict.Count > 0)
                    {
                        var user = CreateProfileInfoFromDictionary(profileDict);
                        collection.Add(user);
                    }
                });
                var profileCount = connection.Wait(profileCountTask);
                int.TryParse(profileCount, out totalRecords);
                return collection;
            }
            catch (Exception ex)
            {
                if (_writeExceptionsToEventLog)
                {
                    WriteToEventLog(ex, "GetAllProfiles");
                    throw new ProviderException(EXCEPTION_MESSAGE);
                }
                throw;
            }
        }

        public override ProfileInfoCollection GetAllInactiveProfiles(ProfileAuthenticationOption authenticationOption, DateTime userInactiveSinceDate, int pageIndex, int pageSize, out int totalRecords)
        {
            throw new NotImplementedException();
        }

        public override ProfileInfoCollection FindProfilesByUserName(ProfileAuthenticationOption authenticationOption, string usernameToMatch, int pageIndex, int pageSize, out int totalRecords)
        {
            throw new NotImplementedException();
        }

        public override ProfileInfoCollection FindInactiveProfilesByUserName(ProfileAuthenticationOption authenticationOption, string usernameToMatch, DateTime userInactiveSinceDate, int pageIndex, int pageSize, out int totalRecords)
        {
            throw new NotImplementedException();
        }

        public override int GetNumberOfInactiveProfiles(ProfileAuthenticationOption authenticationOption, DateTime userInactiveSinceDate)
        {
            throw new NotImplementedException();
        }

        private ProfileInfo CreateProfileInfoFromDictionary(Dictionary<string, byte[]> dict)
        {
            var profileInfo = new ProfileInfo(new string(Encoding.Unicode.GetChars(dict["Username"])),
                BitConverter.ToBoolean(dict["IsAnonymous"], 0),
                DateTime.FromBinary(BitConverter.ToInt64(dict["LastActivityDate"], 0)),
                DateTime.FromBinary(BitConverter.ToInt64(dict["LastUpdatedDate"], 0)),
                0);

            return profileInfo;
        }

        private string GetProfileKey(string profileName)
        {
            return string.Format("application:{0}:profile:{1}", ApplicationName, profileName.ToLower());
        }

        private string GetProfilesKey()
        {
            return string.Format("application:{0}:profiles", ApplicationName);
        }

        private string GetProfilesKeyAuthenticated()
        {
            return string.Format("application:{0}:profiles:authenticated", ApplicationName);
        }

        private string GetProfilesKeyAnonymous()
        {
            return string.Format("application:{0}:profiles:anonymous", ApplicationName);
        }

        private string GetProfilesCountKey()
        {
            return string.Format("application:{0}:profilecount", ApplicationName);
        }

        private static void WriteToEventLog(Exception ex, string action)
        {
            var log = new EventLog { Source = "RedisProfileProvider", Log = "Application" };
            var message = "Action: " + action + "\n\n";
            message += "Exception: " + ex;
            log.WriteEntry(message);
        }

        private RedisConnection GetConnection()
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
                WriteToEventLog(args.Exception, string.Format("Sender: {0}\n\nCause: {1}", sender, args.Cause));
        }
    }
}
