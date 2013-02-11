using System;
using System.Collections.Specialized;
using System.Configuration.Provider;
using System.Diagnostics;
using System.Net.Sockets;
using System.Web.Management;
using System.Web.Script.Serialization;
using BookSleeve;

namespace RedisProviders
{
    public class RedisWebEventProvider : WebEventProvider
    {
        private const string EXCEPTION_MESSAGE = "An exception occurred. Please check the Event Log.";
        private const string REDIS_CONNECTION_FAILED = "Redis connection failed.";

        private int _redisDb;
        private string _host;
        private int _port;
        private string _password;
        private string _name;
        private bool _writeExceptionsToEventLog;

        private static RedisConnection _connection;

        public override void Initialize(string name, NameValueCollection config)
        {
            if (config == null)
                throw new ArgumentNullException("config");

            if (string.IsNullOrEmpty(name))
                name = "RedisWebEventProvider";
            _name = name;

            base.Initialize(name, config);

            _host = string.IsNullOrEmpty(config["host"]) ? "localhost" : config["host"];
            _port = string.IsNullOrEmpty(config["port"]) ? 6379 : int.Parse(config["port"]);
            _password = !string.IsNullOrEmpty(config["password"]) ? null : config["password"];
            _redisDb = string.IsNullOrEmpty(config["db"]) ? 0 : int.Parse(config["db"]);

            if (config["writeExceptionsToEventLog"] != null)
            {
                if (config["writeExceptionsToEventLog"].ToUpper() == "TRUE")
                {
                    _writeExceptionsToEventLog = true;
                }
            }
        }

        public override void ProcessEvent(WebBaseEvent raisedEvent)
        {
            try
            {
                var serializer = new JavaScriptSerializer();
                var json = serializer.Serialize(raisedEvent);
                var connection = GetConnection();
                connection.Lists.AddFirst(_redisDb, Key(), json);
            }
            catch (Exception ex)
            {
                if (_writeExceptionsToEventLog)
                {
                    WriteToEventLog(ex, "ProcessEvent");
                    throw new ProviderException(EXCEPTION_MESSAGE);
                }
                throw;
            }
        }

        public override void Shutdown() { }

        public override void Flush() { }

        private static void WriteToEventLog(Exception e, string action)
        {
            var log = new EventLog { Source = "RedisRoleProvider", Log = "Application" };

            var message = "Action: " + action + "\n\n";
            message += "Exception: " + e;

            log.WriteEntry(message);
        }

        private string Key()
        {
            return string.Format("application:{0}:webevents", _name);
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
                WriteToEventLog(args.Exception, string.Format("Sender: {0}\n\nCause: {1}", sender, args.Cause));
        }
    }
}
