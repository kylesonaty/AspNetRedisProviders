using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Configuration.Provider;
using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Configuration;
using System.Web.Hosting;
using System.Web.Security;
using BookSleeve;

namespace RedisProviders
{
    public class RedisMembershipProvider : MembershipProvider
    {
        private const int NEW_PASSWORD_LENGTH = 8;

        private const string REDIS_CONNECTION_FAILED = "Redis connection failed.";
        private const string REDIS_MEMBERSHIP_PROVIDER = "Redis Membership Provider";
        private const string EXCEPTION_MESSAGE = "An exception occurred. Please check the Event Log.";

        private static RedisConnection _connection;

        private string _applicationName;
        private int _maxInvalidPasswordAttempts;
        private int _passwordAttemptWindow;
        private int _minRequiredNonAlphanumericCharacters;
        private int _minRequiredPasswordLength;
        private string _passwordStrengthRegularExpression;
        private bool _enablePasswordReset;
        private bool _enablePasswordRetrieval;
        private bool _requiresQuestionAndAnswer;
        private bool _requiresUniqueEmail;
        private bool _writeExceptionsToEventLog;
        private MembershipPasswordFormat _passwordFormat;
        private MachineKeySection _machineKey;
        private int _redisDb;
        private string _host;
        private int _port;
        private string _password;

        public override string ApplicationName { get { return _applicationName; } set { _applicationName = value; } }
        public override bool EnablePasswordReset { get { return _enablePasswordReset; } }
        public override bool EnablePasswordRetrieval { get { return _enablePasswordRetrieval; } }
        public override bool RequiresQuestionAndAnswer { get { return _requiresQuestionAndAnswer; } }
        public override bool RequiresUniqueEmail { get { return _requiresUniqueEmail; } }
        public override int MaxInvalidPasswordAttempts { get { return _maxInvalidPasswordAttempts; } }
        public override int PasswordAttemptWindow { get { return _passwordAttemptWindow; } }
        public override MembershipPasswordFormat PasswordFormat { get { return _passwordFormat; } }
        public override int MinRequiredNonAlphanumericCharacters { get { return _minRequiredNonAlphanumericCharacters; } }
        public override int MinRequiredPasswordLength { get { return _minRequiredPasswordLength; } }
        public override string PasswordStrengthRegularExpression { get { return _passwordStrengthRegularExpression; } }

        public override void Initialize(string name, NameValueCollection config)
        {
            if (config == null)
                throw new ArgumentNullException("config");

            if (string.IsNullOrEmpty(name))
                name = "RedisMembershipProvider";

            config.Remove("description");
            config.Add("description", REDIS_MEMBERSHIP_PROVIDER);

            base.Initialize(name, config);

            _host = GetConfigValue(config["host"], Defaults.Host);
            _port = Convert.ToInt32(GetConfigValue(config["port"], Defaults.Port));
            _password = GetConfigValue(config["password"], null);
            _redisDb = Convert.ToInt32(GetConfigValue(config["db"], Defaults.Db)); 
            _applicationName = GetConfigValue(config["applicationName"], HostingEnvironment.ApplicationVirtualPath);
            _maxInvalidPasswordAttempts = Convert.ToInt32(GetConfigValue(config["maxInvalidPasswordAttempts"], "5"));
            _passwordAttemptWindow = Convert.ToInt32(GetConfigValue(config["passwordAttemptWindow"], "10"));
            _minRequiredNonAlphanumericCharacters = Convert.ToInt32(GetConfigValue(config["minRequiredNonAlphanumericCharacters"], "1"));
            _minRequiredPasswordLength = Convert.ToInt32(GetConfigValue(config["minRequiredPasswordLength"], "7"));
            _passwordStrengthRegularExpression = Convert.ToString(GetConfigValue(config["passwordStrengthRegularExpression"], ""));
            _enablePasswordReset = Convert.ToBoolean(GetConfigValue(config["enablePasswordReset"], "true"));
            _enablePasswordRetrieval = Convert.ToBoolean(GetConfigValue(config["enablePasswordRetrieval"], "true"));
            _requiresQuestionAndAnswer = Convert.ToBoolean(GetConfigValue(config["requiresQuestionAndAnswer"], "false"));
            _requiresUniqueEmail = Convert.ToBoolean(GetConfigValue(config["requiresUniqueEmail"], "true"));
            _writeExceptionsToEventLog = Convert.ToBoolean(GetConfigValue(config["writeExceptionsToEventLog"], "true"));

            var tempFormat = config["passwordFormat"] ?? "Hashed";

            switch (tempFormat)
            {
                case "Hashed":
                    _passwordFormat = MembershipPasswordFormat.Hashed;
                    break;
                case "Encrypted":
                    _passwordFormat = MembershipPasswordFormat.Encrypted;
                    break;
                case "Clear":
                    _passwordFormat = MembershipPasswordFormat.Clear;
                    break;
                default:
                    throw new ProviderException("Password format not supported.");
            }

            // Get encryption and decryption key information from the configuration.
            var cfg = WebConfigurationManager.OpenWebConfiguration(HostingEnvironment.ApplicationVirtualPath);
            _machineKey = (MachineKeySection)cfg.GetSection("system.web/machineKey");

            if (_machineKey.ValidationKey.Contains("AutoGenerate"))
                if (PasswordFormat != MembershipPasswordFormat.Clear)
                    throw new ProviderException("Hashed or Encrypted passwords are not supported with auto-generated keys.");
        }

        private static string GetConfigValue(string configValue, string defaultValue)
        {
            return string.IsNullOrEmpty(configValue) ? defaultValue : configValue;
        }

        public override bool ChangePassword(string username, string oldPassword, string newPassword)
        {
            if (!ValidateUser(username, oldPassword))
                return false;

            var args = new ValidatePasswordEventArgs(username, newPassword, true);
            OnValidatingPassword(args);
            if (args.Cancel)
                if (args.FailureInformation != null)
                    throw args.FailureInformation;
                else
                    throw new MembershipPasswordException("Change password canceled due to new password validation failure.");

            try
            {
                var connection = GetConnection();
                connection.Hashes.Set(_redisDb, GetUserKey(username), "Password", EncodePassword(newPassword));
                connection.Hashes.Set(_redisDb, GetUserKey(username), "LastPasswordChangedDate", DateTime.UtcNow.ToString(CultureInfo.InvariantCulture));
                return true;
            }
            catch (Exception ex)
            {
                if (_writeExceptionsToEventLog)
                {
                    WriteToEventLog(ex, "ChangePassword");
                    return false;
                }
                throw;
            }
        }

        public override bool ChangePasswordQuestionAndAnswer(string username, string password, string newPasswordQuestion, string newPasswordAnswer)
        {
            if (!ValidateUser(username, password))
                return false;

            try
            {
                var connection = GetConnection();
                connection.Hashes.Set(_redisDb, GetUserKey(username), "Question", newPasswordQuestion);
                connection.Hashes.Set(_redisDb, GetUserKey(username), "Answer", EncodePassword(newPasswordAnswer));
                return true;
            }
            catch (Exception ex)
            {
                if (_writeExceptionsToEventLog)
                {
                    WriteToEventLog(ex, "ChangePasswordQuestionAndAnswer");
                    return false;
                }
                throw;
            }
        }

        public override MembershipUser CreateUser(string username, string password, string email, string passwordQuestion, string passwordAnswer, bool isApproved, object providerUserKey, out MembershipCreateStatus status)
        {
            var args = new ValidatePasswordEventArgs(username, password, true);

            OnValidatingPassword(args);

            if (args.Cancel)
            {
                status = MembershipCreateStatus.InvalidPassword;
                return null;
            }

            if (RequiresUniqueEmail && GetUserNameByEmail(email) != null)
            {
                status = MembershipCreateStatus.DuplicateEmail;
                return null;
            }

            var u = GetUser(username, false);

            if (u != null)
            {
                status = MembershipCreateStatus.DuplicateUserName;
                return null;
            }

            if (providerUserKey == null)
            {
                providerUserKey = Guid.NewGuid();
            }
            else
            {
                if (!(providerUserKey is Guid))
                {
                    status = MembershipCreateStatus.InvalidProviderUserKey;
                    return null;
                }
            }

            try
            {
                var createDate = DateTime.Now;
                var user = CreateUserDictionary(username, password, email, passwordQuestion, passwordAnswer, isApproved, (Guid)providerUserKey, createDate);
                var connection = GetConnection();
                connection.Hashes.Set(_redisDb, GetUserKey(username), user);
                connection.Strings.Set(_redisDb, GetProviderKey(providerUserKey), username);
                connection.Lists.AddLast(_redisDb, GetUsersKey(), Encoding.Unicode.GetBytes(username));
                connection.Strings.Increment(_redisDb, GetUsersCountKey());
                if (!string.IsNullOrEmpty(email))
                    connection.Strings.Set(_redisDb, GetEmailKey(email), username);
                status = MembershipCreateStatus.Success;
            }
            catch (Exception ex)
            {
                if (_writeExceptionsToEventLog)
                {
                    WriteToEventLog(ex, "CreateUser");
                }

                status = MembershipCreateStatus.ProviderError;
            }
            return GetUser(username, false);
        }

        public override bool DeleteUser(string username, bool deleteAllRelatedData)
        {
            try
            {
                var connection = GetConnection();
                var user = GetUser(username, false);
                connection.Keys.Remove(_redisDb, GetUserKey(username));

                if (deleteAllRelatedData)
                {
                    if (user != null)
                    {
                        connection.Keys.Remove(_redisDb, GetEmailKey(user.Email));
                        connection.Keys.Remove(_redisDb, GetProviderKey(user.ProviderUserKey));
                        connection.Lists.Remove(_redisDb, GetUsersKey(), username);
                        connection.Strings.Decrement(_redisDb, GetUsersCountKey());
                        connection.SortedSets.Remove(_redisDb, GetUsersOnlineKey(), username);
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                if (_writeExceptionsToEventLog)
                {
                    WriteToEventLog(ex, "DeleteUser");
                    return false;
                }
                throw;
            }
        }

        public override MembershipUserCollection FindUsersByEmail(string emailToMatch, int pageIndex, int pageSize, out int totalRecords)
        {
            throw new NotImplementedException();
        }

        public override MembershipUserCollection FindUsersByName(string usernameToMatch, int pageIndex, int pageSize, out int totalRecords)
        {
            throw new NotImplementedException();
        }

        public override MembershipUserCollection GetAllUsers(int pageIndex, int pageSize, out int totalRecords)
        {
            try
            {
                var connection = GetConnection();
                var start = pageIndex * pageSize;
                var end = pageIndex * pageSize + pageSize;
                var usersTask = connection.Lists.Range(_redisDb, GetUsersKey(), start, end);
                var userCountTask = connection.Strings.GetString(_redisDb, GetUsersCountKey());
                var collection = new MembershipUserCollection();
                var usernames = connection.Wait(usersTask);
                Parallel.ForEach(usernames, result =>
                    {
                        var username = new string(Encoding.Unicode.GetChars(result));
                        var userTask = connection.Hashes.GetAll(_redisDb, GetUserKey(username));
                        var userDict = connection.Wait(userTask);
                        if (userDict.Count > 0)
                        {
                            var user = CreateMembershipUserFromDictionary(userDict);
                            collection.Add(user);
                        }
                    });
                var userCount = connection.Wait(userCountTask);
                int.TryParse(userCount, out totalRecords);
                return collection;
            }
            catch (Exception ex)
            {
                if (_writeExceptionsToEventLog)
                {
                    WriteToEventLog(ex, "GetAllUsers");
                    throw new ProviderException(EXCEPTION_MESSAGE);
                }
                throw;
            }
        }

        public override int GetNumberOfUsersOnline()
        {
            var timespan = TimeSpan.FromMinutes(Membership.UserIsOnlineTimeWindow);
            var connection = GetConnection();
            var min = DateTime.UtcNow.Subtract(timespan).ToBinary();
            var max = DateTime.UtcNow.ToBinary();
            var task = connection.SortedSets.GetLength(_redisDb, GetUsersOnlineKey(), min, max);
            var result = connection.Wait(task);
            return (int)result;
        }

        public override string GetPassword(string username, string answer)
        {
            if (!EnablePasswordRetrieval)
            {
                throw new ProviderException("Password Retrieval Not Enabled.");
            }

            if (PasswordFormat == MembershipPasswordFormat.Hashed)
            {
                throw new ProviderException("Cannot retrieve Hashed passwords.");
            }

            string password;
            string passwordAnswer;

            try
            {
                var connection = GetConnection();
                var passwordTask = connection.Hashes.Get(_redisDb, GetUserKey(username), new[] { "Password", "PasswordAnswer", "IsLockedOut" });
                var results = connection.Wait(passwordTask);

                if (results == null)
                    throw new MembershipPasswordException("The supplied user name is not found.");

                password = BitConverter.ToString(results[0]);
                passwordAnswer = BitConverter.ToString(results[1]);
                var isLockedOut = BitConverter.ToBoolean(results[2], 0);

                if (isLockedOut)
                    throw new MembershipPasswordException("The supplied user is locked out.");
            }
            catch (Exception ex)
            {
                if (_writeExceptionsToEventLog)
                {
                    WriteToEventLog(ex, "GetPassword");
                    throw new ProviderException(EXCEPTION_MESSAGE);
                }
                throw;
            }

            if (RequiresQuestionAndAnswer && !CheckPassword(answer, passwordAnswer))
            {
                UpdateFailureCount(username, "passwordAnswer");
                throw new MembershipPasswordException("Incorrect password answer.");
            }

            if (PasswordFormat == MembershipPasswordFormat.Encrypted)
            {
                password = UnEncodePassword(password);
            }

            return password;
        }

        public override MembershipUser GetUser(string username, bool userIsOnline)
        {
            try
            {
                var connection = GetConnection();
                var dictTask = connection.Hashes.GetAll(_redisDb, GetUserKey(username));
                var dict = connection.Wait(dictTask);
                if (dict.Count > 0)
                {
                    var user = CreateMembershipUserFromDictionary(dict);
                    if (userIsOnline)
                        SetLastActivityDate(username, connection);
                    return user;
                }
                return null;
            }
            catch (Exception e)
            {
                if (_writeExceptionsToEventLog)
                {
                    WriteToEventLog(e, "GetUser(Object, Boolean)");
                    throw new ProviderException(EXCEPTION_MESSAGE);
                }
                throw;
            }
        }

        private void SetLastActivityDate(string username, RedisConnection connection)
        {
            var dateTime = DateTime.UtcNow.ToBinary();
            connection.Hashes.Set(_redisDb, GetUserKey(username), "LastActivityDate", BitConverter.GetBytes(dateTime));
            connection.SortedSets.Add(_redisDb, GetUsersOnlineKey(), username, dateTime);
        }

        public override MembershipUser GetUser(object providerUserKey, bool userIsOnline)
        {
            try
            {
                var connection = GetConnection();
                var usernameTask = connection.Strings.GetString(_redisDb, GetProviderKey(providerUserKey));
                var username = connection.Wait(usernameTask);
                return GetUser(username, userIsOnline);
            }
            catch (Exception ex)
            {
                if (_writeExceptionsToEventLog)
                {
                    WriteToEventLog(ex, "GetUser(Object, Boolean)");
                    throw new ProviderException(EXCEPTION_MESSAGE);
                }
                throw;
            }
        }

        public override string GetUserNameByEmail(string email)
        {
            try
            {
                var connection = GetConnection();
                var getTask = connection.Strings.GetString(_redisDb, GetEmailKey(email));
                var userName = connection.Wait(getTask);
                return userName;
            }
            catch (Exception ex)
            {
                if (_writeExceptionsToEventLog)
                {
                    WriteToEventLog(ex, "GetUserNameByEmail");
                    throw new ProviderException(EXCEPTION_MESSAGE);
                }
                throw;
            }
        }

        public override string ResetPassword(string username, string answer)
        {
            if (!EnablePasswordReset)
                throw new NotSupportedException("Password reset is not enabled.");

            if (answer == null && RequiresQuestionAndAnswer)
            {
                UpdateFailureCount(username, "passwordAnswer");
                throw new ProviderException("Password answer required for password reset.");
            }

            var newPassword = Membership.GeneratePassword(NEW_PASSWORD_LENGTH, MinRequiredNonAlphanumericCharacters);
            var args = new ValidatePasswordEventArgs(username, newPassword, true);

            OnValidatingPassword(args);

            if (args.Cancel)
                if (args.FailureInformation != null)
                    throw args.FailureInformation;
                else
                    throw new MembershipPasswordException("Reset password canceled due to password validation failure.");

            try
            {
                var connection = GetConnection();
                var getTask = connection.Hashes.GetString(_redisDb, GetUserKey(username), new[] { "PasswordAnswer", "IsLockedOut" });
                var result = connection.Wait(getTask);

                if (result == null || result.Length != 2)
                    throw new MembershipPasswordException("User not found, or user is locked out. Password not Reset.");

                var passwordAnswer = result[0];
                var isLockedOutString = result[1];
                bool isLockedOut;
                Boolean.TryParse(isLockedOutString, out isLockedOut);

                if (isLockedOut)
                    throw new MembershipPasswordException("The supplied user is locked out.");

                if (RequiresQuestionAndAnswer && !CheckPassword(answer, passwordAnswer))
                {
                    UpdateFailureCount(username, "passwordAnswer");
                    throw new MembershipPasswordException("Incorrect password answer.");
                }

                connection.Hashes.Set(_redisDb, GetUserKey(username), "Password", EncodePassword(newPassword));
                connection.Hashes.Set(_redisDb, GetUserKey(username), "LastPasswordChangedDate", BitConverter.GetBytes(DateTime.UtcNow.ToBinary()));

                return newPassword;
            }
            catch (Exception ex)
            {
                if (_writeExceptionsToEventLog)
                {
                    WriteToEventLog(ex, "ResetPassword");
                    throw new ProviderException(EXCEPTION_MESSAGE);
                }
                throw;
            }
        }

        public override bool UnlockUser(string username)
        {
            try
            {
                var connection = GetConnection();
                var dict = new Dictionary<string, byte[]>
                    {
                        {"IsLockedOut", BitConverter.GetBytes(false)},
                        {"LastLockedOutDate", BitConverter.GetBytes(DateTime.UtcNow.ToBinary())},
                    };
                connection.Hashes.Set(_redisDb, GetUserKey(username), dict);
                return true;
            }
            catch (Exception ex)
            {
                if (_writeExceptionsToEventLog)
                {
                    WriteToEventLog(ex, "UnlockUser");
                    throw new ProviderException(EXCEPTION_MESSAGE);
                }
                throw;
            }
        }

        public override void UpdateUser(MembershipUser user)
        {
            try
            {
                var connection = GetConnection();
                var dict = new Dictionary<string, byte[]>
                    {
                        {"Email", Encoding.Unicode.GetBytes(user.Email)},
                        {"Comment", Encoding.Unicode.GetBytes(user.Comment)},
                        {"IsApproved", BitConverter.GetBytes(user.IsApproved)}
                    };
                connection.Hashes.Set(_redisDb, GetUserKey(user.UserName), dict);
            }
            catch (Exception ex)
            {
                if (_writeExceptionsToEventLog)
                {
                    WriteToEventLog(ex, "UpdateUser");
                    throw new ProviderException(EXCEPTION_MESSAGE);
                }
                throw;
            }
        }

        public override bool ValidateUser(string username, string password)
        {
            try
            {
                var isValid = false;

                var connection = GetConnection();
                var getUserInfoTask = connection.Hashes.Get(_redisDb, GetUserKey(username), new[] { "Password", "IsApproved", "IsLockedOut" });
                var results = connection.Wait(getUserInfoTask);

                if (results == null || results.Length != 3 || results[0] == null)
                    return false; // didn't find the user

                var pwd = new string(Encoding.Unicode.GetChars(results[0]));
                var isApproved = BitConverter.ToBoolean(results[1], 0);
                var isLockedOut = BitConverter.ToBoolean(results[2], 0);

                if (isLockedOut)
                    return false;

                if (CheckPassword(password, pwd))
                {
                    if (isApproved)
                    {
                        isValid = true;
                        connection.Hashes.Set(_redisDb, GetUserKey(username), "LastLoginDate", BitConverter.GetBytes(DateTime.UtcNow.ToBinary()));
                    }
                }
                else
                {
                    UpdateFailureCount(username, "password");
                }

                return isValid;
            }
            catch (Exception ex)
            {
                if (_writeExceptionsToEventLog)
                {
                    WriteToEventLog(ex, "ValidateUser");
                    return false;
                }
                throw;
            }
        }

        private void UpdateFailureCount(string username, string failureType)
        {
            try
            {
                var connection = GetConnection();
                var getFailureInfoTask = connection.Hashes.Get(_redisDb, GetUserKey(username),
                                                               new[]
                                                                   {
                                                                       "FailedPasswordAttemptCount",
                                                                       "FailedPasswordAttemptWindowStart",
                                                                       "FailedPasswordAnswerAttemptCount",
                                                                       "FailedPasswordAnswerAttemptWindowStart"
                                                                   });
                var results = connection.Wait(getFailureInfoTask);
                var windowStart = new DateTime();
                var failureCount = 0;

                if (results != null)
                {
                    if (failureType == "password")
                    {
                        failureCount = BitConverter.ToInt32(results[0], 0);
                        windowStart = DateTime.FromBinary(BitConverter.ToInt64(results[1], 0));
                    }

                    if (failureType == "passwordAnswer")
                    {
                        failureCount = BitConverter.ToInt32(results[2], 0);
                        windowStart = DateTime.FromBinary(BitConverter.ToInt64(results[3], 0));
                    }
                }

                var windowEnd = windowStart.AddMinutes(PasswordAttemptWindow);

                if (failureCount == 0 || DateTime.UtcNow > windowEnd)
                {
                    // First password failure or outside of PasswordAttemptWindow. 
                    // Start a new password failure count from 1 and a new window starting now.
                    if (failureType == "password")
                    {
                        var dict = new Dictionary<string, byte[]>
                            {
                                {"FailedPasswordAttemptCount", BitConverter.GetBytes(1)},
                                {"FailedPasswordAttemptWindowStart", BitConverter.GetBytes(DateTime.UtcNow.ToBinary())}
                            };
                        connection.Hashes.Set(_redisDb, GetUserKey(username), dict);
                    }

                    if (failureType == "passwordAnswer")
                    {
                        var dict = new Dictionary<string, byte[]>
                            {
                                {"FailedPasswordAnswerAttemptCount", BitConverter.GetBytes(1)},
                                {"FailedPasswordAnswerAttemptWindowStart", BitConverter.GetBytes(DateTime.UtcNow.ToBinary())}
                            };
                        connection.Hashes.Set(_redisDb, GetUserKey(username), dict);
                    }
                }
                else
                {
                    if (failureCount++ >= MaxInvalidPasswordAttempts)
                    {
                        // Password attempts have exceeded the failure threshold. Lock out the user.
                        connection.Hashes.Set(_redisDb, GetUserKey(username), "IsLockedOut", BitConverter.GetBytes(true));
                    }
                    else
                    {
                        // Password attempts have not exceeded the failure threshold. Update the failure counts. Leave the window the same.
                        if (failureType == "password")
                            connection.Hashes.Set(_redisDb, GetUserKey(username), "FailedPasswordAttemptCount", BitConverter.GetBytes(failureCount));
                        if (failureType == "passwordAnswer")
                            connection.Hashes.Set(_redisDb, GetUserKey(username), "FailedPasswordAnswerAttemptCount", BitConverter.GetBytes(failureCount));
                    }
                }
            }
            catch (Exception ex)
            {
                if (_writeExceptionsToEventLog)
                {
                    WriteToEventLog(ex, "UpdateFailureCount");
                    throw new ProviderException(EXCEPTION_MESSAGE);
                }
                throw;
            }
        }


        private bool CheckPassword(string password, string dbpassword)
        {
            var pass1 = password;
            var pass2 = dbpassword;

            switch (PasswordFormat)
            {
                case MembershipPasswordFormat.Encrypted:
                    pass2 = UnEncodePassword(dbpassword);
                    break;
                case MembershipPasswordFormat.Hashed:
                    pass1 = EncodePassword(password);
                    break;
            }

            return pass1 == pass2;
        }

        private string EncodePassword(string password)
        {
            var encodedPassword = password;

            switch (PasswordFormat)
            {
                case MembershipPasswordFormat.Clear:
                    break;
                case MembershipPasswordFormat.Encrypted:
                    encodedPassword = Convert.ToBase64String(EncryptPassword(Encoding.Unicode.GetBytes(password)));
                    break;
                case MembershipPasswordFormat.Hashed:
                    var hash = new HMACSHA1 { Key = HexToByte(_machineKey.ValidationKey) };
                    encodedPassword =
                      Convert.ToBase64String(hash.ComputeHash(Encoding.Unicode.GetBytes(password)));
                    break;
                default:
                    throw new ProviderException("Unsupported password format.");
            }

            return encodedPassword;
        }

        private string UnEncodePassword(string encodedPassword)
        {
            string password = encodedPassword;

            switch (PasswordFormat)
            {
                case MembershipPasswordFormat.Clear:
                    break;
                case MembershipPasswordFormat.Encrypted:
                    password =
                      Encoding.Unicode.GetString(DecryptPassword(Convert.FromBase64String(password)));
                    break;
                case MembershipPasswordFormat.Hashed:
                    throw new ProviderException("Cannot unencode a hashed password.");
                default:
                    throw new ProviderException("Unsupported password format.");
            }

            return password;
        }

        private static byte[] HexToByte(string hexString)
        {
            var returnBytes = new byte[hexString.Length / 2];
            for (var i = 0; i < returnBytes.Length; i++)
                returnBytes[i] = Convert.ToByte(hexString.Substring(i * 2, 2), 16);
            return returnBytes;
        }

        private static void WriteToEventLog(Exception ex, string action)
        {
            var log = new EventLog { Source = "RedisMembershipProvider", Log = "Application" };
            var message = "Action: " + action + "\n\n";
            message += "Exception: " + ex;
            log.WriteEntry(message);
        }

        public MembershipUser CreateMembershipUserFromDictionary(Dictionary<string, byte[]> dict)
        {
            var membershipUser = new MembershipUser(Name,
                                                    new string(Encoding.Unicode.GetChars(dict["Username"])),
                                                    new Guid(dict["Id"]),
                                                    new string(Encoding.Unicode.GetChars(dict["Email"])),
                                                    new string(Encoding.Unicode.GetChars(dict["PasswordQuestion"])),
                                                    new string(Encoding.Unicode.GetChars(dict["Comment"])),
                                                    BitConverter.ToBoolean(dict["IsApproved"], 0),
                                                    BitConverter.ToBoolean(dict["IsLockedOut"], 0),
                                                    DateTime.FromBinary(BitConverter.ToInt64(dict["CreationDate"], 0)),
                                                    DateTime.FromBinary(BitConverter.ToInt64(dict["LastLoginDate"], 0)),
                                                    DateTime.FromBinary(BitConverter.ToInt64(dict["LastActivityDate"], 0)),
                                                    DateTime.FromBinary(BitConverter.ToInt64(dict["LastPasswordChangeDate"], 0)),
                                                    DateTime.FromBinary(BitConverter.ToInt64(dict["LastLockedOutDate"], 0)));
            return membershipUser;
        }

        public Dictionary<string, byte[]> CreateUserDictionary(string username, string password, string email, string passwordQuestion, string passwordAnswer,
                          bool isApproved, Guid providerUserKey, DateTime createDate)
        {
            var dateBytes = BitConverter.GetBytes(createDate.ToBinary());
            var countBytes = BitConverter.GetBytes(0);

            var dict = new Dictionary<string, byte[]>
                {
                    {"Id", providerUserKey.ToByteArray()},
                    {"Username", Encoding.Unicode.GetBytes(username)},
                    {"Password", Encoding.Unicode.GetBytes(EncodePassword(password))},
                    {"Email", email != null ? Encoding.Unicode.GetBytes(email) : Encoding.Unicode.GetBytes(string.Empty)},
                    {"PasswordQuestion", passwordQuestion != null ? Encoding.Unicode.GetBytes(passwordQuestion) : Encoding.Unicode.GetBytes(string.Empty)},
                    {"PasswordAnswer", passwordAnswer != null ?  Encoding.Unicode.GetBytes(passwordAnswer) : Encoding.Unicode.GetBytes(string.Empty)},
                    {"IsApproved", BitConverter.GetBytes(isApproved)},
                    {"Comment", Encoding.Unicode.GetBytes(string.Empty)},
                    {"CreationDate", dateBytes},
                    {"LastPasswordChangeDate", dateBytes},
                    {"LastActivityDate", dateBytes},
                    {"LastLoginDate", dateBytes},
                    {"IsLockedOut", BitConverter.GetBytes(false)},
                    {"LastLockedOutDate", dateBytes},
                    {"FailedPasswordAttemptCount", countBytes},
                    {"FailedPasswordAttemptWindowStart", dateBytes},
                    {"FailedPasswordAnswerAttemptCount", countBytes},
                    {"FailedPasswordAnswerAttemptWindowStart", dateBytes}
                };
            return dict;
        }
        private string GetUserKey(string username)
        {
            return string.Format("application:{0}:user:{1}", ApplicationName, username.ToLower());
        }

        private string GetEmailKey(string email)
        {
            return string.Format("application:{0}:user:email:{1}", ApplicationName, email);
        }

        private string GetProviderKey(object providerKey)
        {
            return string.Format("application:{0}:user:providerkey:{1}", ApplicationName, providerKey);
        }

        private string GetUsersKey()
        {
            return string.Format("application:{0}:users", ApplicationName);
        }

        private string GetUsersCountKey()
        {
            return string.Format("application:{0}:usercount", ApplicationName);
        }

        private string GetUsersOnlineKey()
        {
            return string.Format("application:{0}:useronline", ApplicationName);
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
