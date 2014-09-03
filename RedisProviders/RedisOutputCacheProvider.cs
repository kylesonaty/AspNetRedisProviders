using System;
using System.Collections.Specialized;
using System.Configuration.Provider;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Web.Caching;
using BookSleeve;
using ErrorEventArgs = BookSleeve.ErrorEventArgs;

namespace RedisProviders
{
    class RedisOutputCacheProvider : OutputCacheProvider
    {
        private const string EVENT_SOURCE = "RedisOutputCacheProvider";
        private const string EVENT_LOG = "Application";
        private const string REDIS_CONNECTION_FAILED = "Redis connection failed.";
        private const string EXCEPTION_MESSAGE = "An exception occurred. Please check the Event Log.";

        private int _redisDb;
        private string _host;
        private int _port;
        private string _password;
        private bool _writeExceptionsToEventLog;

        private static RedisConnection _connection;

        public override void Initialize(string name, NameValueCollection config)
        {
            if (config == null)
                throw new ArgumentNullException("config");

            if (string.IsNullOrEmpty(name))
                name = "RedisOutputCacheProvider";

            base.Initialize(name, config);

            _host = GetConfigValue(config["host"], Defaults.Host);
            _port = Convert.ToInt32(GetConfigValue(config["port"], Defaults.Port));
            _password = GetConfigValue(config["password"], null);
            _redisDb = Convert.ToInt32(GetConfigValue(config["db"], Defaults.Db));
            _writeExceptionsToEventLog = Convert.ToBoolean(GetConfigValue(config["writeExceptionsToEventLog"], "false"));
        }

        private static string GetConfigValue(string configValue, string defaultValue)
        {
            return string.IsNullOrEmpty(configValue) ? defaultValue : configValue;
        }


        public override object Get(string key)
        {
            var connection = GetConnection();
            var task = connection.Strings.Get(_redisDb, key);
            var result = connection.Wait(task);
            if (result == null)
                return null;
            var ms = new MemoryStream(result);
            var formatter = new BinaryFormatter();
            return formatter.Deserialize(ms);
        }

        public override object Add(string key, object entry, DateTime utcExpiry)
        {
            Set(key, entry, utcExpiry);
            return entry;
        }

        public override void Set(string key, object entry, DateTime utcExpiry)
        {
            var connection = GetConnection();
            IFormatter formatter = new BinaryFormatter();
            var ms = new MemoryStream();
            formatter.Serialize(ms, entry);
            connection.Strings.Set(_redisDb, key, ms.ToArray());
            var seconds = utcExpiry == DateTime.MaxValue ? int.MaxValue : Convert.ToInt32(Math.Round(utcExpiry.Subtract(DateTime.UtcNow).TotalSeconds, 0));
            connection.Keys.Expire(_redisDb, key, seconds);
        }

        public override void Remove(string key)
        {
            var connection = GetConnection();
            connection.Keys.Remove(_redisDb, key);
        }

        private static void WriteToEventLog(Exception ex, string action)
        {
            var log = new EventLog { Source = EVENT_SOURCE, Log = EVENT_LOG };
            var message = "Action: " + action + "\n\n";
            message += "Exception: " + ex;
            log.WriteEntry(message);
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
