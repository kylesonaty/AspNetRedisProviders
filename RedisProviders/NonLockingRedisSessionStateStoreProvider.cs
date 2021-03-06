﻿using System;
using System.Configuration.Provider;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Web;
using System.Web.Configuration;
using System.Web.Hosting;
using System.Web.SessionState;
using BookSleeve;

namespace RedisProviders
{
    public class NonLockingRedisSessionStateStoreProvider : SessionStateStoreProviderBase
    {
        private const string EXCEPTION_MESSAGE = "An exception occurred. Please check the Event Log.";
        private const string REDIS_CONNECTION_FAILED = "Redis connection failed.";

        private int _redisDb;
        private string _host;
        private int _port;
        private string _password;

        private SessionStateSection _sessionStateConfig;
        private bool _writeExceptionsToEventLog;

        private static RedisConnection _connection;

        public string ApplicationName { get; private set; }

        public override void Initialize(string name, System.Collections.Specialized.NameValueCollection config)
        {
            if (config == null)
                throw new ArgumentNullException("config");

            if (string.IsNullOrEmpty(name))
                name = "NonLockingRedisSessionStateStore";

            if (string.IsNullOrEmpty(config["description"]))
            {
                config.Remove("description");
                config.Add("description", "Non-Locking Redis Session State Store Provider");
            }

            base.Initialize(name, config);

            _host = GetConfigValue(config["host"], Defaults.Host);
            _port = Convert.ToInt32(GetConfigValue(config["port"], Defaults.Port));
            _password = GetConfigValue(config["password"], null);
            _redisDb = Convert.ToInt32(GetConfigValue(config["db"], Defaults.Db));
            _writeExceptionsToEventLog = Convert.ToBoolean(GetConfigValue(config["writeExceptionsToEventLog"], "true"));

            ApplicationName = string.IsNullOrEmpty(config["applicationName"]) ? HostingEnvironment.ApplicationVirtualPath : config["applicationName"];
            
            var cfg = WebConfigurationManager.OpenWebConfiguration(ApplicationName);
            _sessionStateConfig = (SessionStateSection)cfg.GetSection("system.web/sessionState");
        }

        private static string GetConfigValue(string configValue, string defaultValue)
        {
            return string.IsNullOrEmpty(configValue) ? defaultValue : configValue;
        }

        public override void Dispose() { }

        public override bool SetItemExpireCallback(SessionStateItemExpireCallback expireCallback)
        {
            return false;
        }

        public override void InitializeRequest(HttpContext context) { }

        public override SessionStateStoreData GetItem(HttpContext context, string id, out bool locked, out TimeSpan lockAge, out object lockId,
                                                      out SessionStateActions actions)
        {
            return GetSessionStoreItem(context, id, out locked, out lockAge, out lockId, out actions);
        }

        public override SessionStateStoreData GetItemExclusive(HttpContext context, string id, out bool locked, out TimeSpan lockAge,
                                                               out object lockId, out SessionStateActions actions)
        {
            return GetSessionStoreItem(context, id, out locked, out lockAge, out lockId, out actions);
        }

        private SessionStateStoreData GetSessionStoreItem(HttpContext context,
                                                          string id,
                                                          out bool locked,
                                                          out TimeSpan lockAge,
                                                          out object lockId,
                                                          out SessionStateActions actions)
        {
            lockAge = TimeSpan.Zero;
            lockId = null;
            locked = false;
            actions = SessionStateActions.None;
            try
            {
                // if no item is found create new data store
                var connection = GetConnection();
                var getTask = connection.Strings.Get(_redisDb, Key(id));
                var data = connection.Wait(getTask);
                return data == null 
                    ? CreateNewStoreData(context, (int)_sessionStateConfig.Timeout.TotalMinutes) 
                    : Deserialize(context, data, (int)_sessionStateConfig.Timeout.TotalMinutes);
            }
            catch (Exception ex)
            {
                if (_writeExceptionsToEventLog)
                {
                    WriteToEventLog(ex, "GetSessionStoreItem");
                    throw new ProviderException(EXCEPTION_MESSAGE);
                }
                throw;
            }
        }

        public override void ReleaseItemExclusive(HttpContext context, string id, object lockId) {} // no op since there is no locking

        public override void SetAndReleaseItemExclusive(HttpContext context, string id, SessionStateStoreData item, object lockId, bool newItem)
        {
            try
            {
                var connection = GetConnection();
                if (item.Items as SessionStateItemCollection != null)
                {
                    var data = Serialize((SessionStateItemCollection)item.Items);
                    connection.Strings.Set(_redisDb, Key(id), data);
                    connection.Keys.Expire(_redisDb, Key(id), (int)_sessionStateConfig.Timeout.TotalMinutes * 60);
                }
            }
            catch (Exception ex)
            {
                if (_writeExceptionsToEventLog)
                {
                    WriteToEventLog(ex, "SetAndReleaseItemExclusive");
                    throw new ProviderException(EXCEPTION_MESSAGE);
                }
                throw;
            }
        }

        public override void RemoveItem(HttpContext context, string id, object lockId, SessionStateStoreData item)
        {
            try
            {
                var connection = GetConnection();
                connection.Keys.Remove(_redisDb, Key(id));    
            }
            catch (Exception ex)
            {
                if (_writeExceptionsToEventLog)
                {
                    WriteToEventLog(ex, "RemoveItem");
                    throw new ProviderException(EXCEPTION_MESSAGE);
                }
                throw;
            }
        }

        public override void ResetItemTimeout(HttpContext context, string id)
        {
            try
            {
                var connection = GetConnection();
                connection.Keys.Expire(_redisDb, Key(id), (int)_sessionStateConfig.Timeout.TotalMinutes * 60);
            }
            catch (Exception ex)
            {
                if (_writeExceptionsToEventLog)
                {
                    WriteToEventLog(ex, "ResetItemTimeout");
                    throw new ProviderException(EXCEPTION_MESSAGE);
                }
                throw;
            }
        }

        public override SessionStateStoreData CreateNewStoreData(HttpContext context, int timeout)
        {
            return new SessionStateStoreData(new SessionStateItemCollection(),
               SessionStateUtility.GetSessionStaticObjects(context),
               timeout);
        }

        public override void CreateUninitializedItem(HttpContext context, string id, int timeout)
        {
            try
            {
                var connection = GetConnection();
                var items = Serialize(new SessionStateItemCollection());
                connection.Strings.Set(_redisDb, Key(id), items);
                connection.Keys.Expire(_redisDb, Key(id), timeout * 60);
            }
            catch (Exception ex)
            {
                if (_writeExceptionsToEventLog)
                {
                    WriteToEventLog(ex, "CreateUninitializedItem");
                    throw new ProviderException(EXCEPTION_MESSAGE);
                }
                throw;
            }
        }

        public override void EndRequest(HttpContext context) { }

        private static byte[] Serialize(SessionStateItemCollection items)
        {
            using (var ms = new MemoryStream())
            {
                using (var writer = new BinaryWriter(ms))
                {
                    if (items != null)
                        items.Serialize(writer);

                    writer.Close();

                    return ms.ToArray();
                }
            }
        }

        private static SessionStateStoreData Deserialize(HttpContext context, byte[] serializedItems, int timeout)
        {
            using (var ms = new MemoryStream(serializedItems))
            {
                var sessionItems = new SessionStateItemCollection();

                if (ms.Length > 0)
                {
                    using (var reader = new BinaryReader(ms))
                    {
                        sessionItems = SessionStateItemCollection.Deserialize(reader);
                    }
                }

                return new SessionStateStoreData(sessionItems, SessionStateUtility.GetSessionStaticObjects(context), timeout);
            }
        }

        private static void WriteToEventLog(Exception e, string action)
        {
            var log = new EventLog { Source = "NonLockingRedisSessionStateStore", Log = "Application" };

            var message = "Action: " + action + "\n\n";
            message += "Exception: " + e;

            log.WriteEntry(message);
        }

        private string Key(string id)
        {
            return string.Format("application:{0}:session:{1}", ApplicationName, id);
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

        private void OnConnectionError(object sender, BookSleeve.ErrorEventArgs args)
        {
            if (_writeExceptionsToEventLog)
            {
                WriteToEventLog(args.Exception, string.Format("Sender: {0}\n\nCause: {1}", sender, args.Cause));
                throw new ProviderException(EXCEPTION_MESSAGE);
            }
        }
    }
}
