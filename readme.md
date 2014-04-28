Redis Providers
---------------------------

The Redis Providers library is a collection of ASP.NET providers that uses redis as the database to store provider information. Currently the following providers are implemented:

* Session State Store Provider
* Role Provider
* Membership Provider
* Web Event Provider

To run an instance of Redis for this project, download and extract Redis for Windows here: https://github.com/MSOpenTech/redis. 
Inside bin\release you'll find a 32-bit and 64-bit zip. Extract either one and run redis-server.exe.

### Redis Session State Store Provider `RedisProviders.RedisSessionStateStoreProvider`

The `RedisSessionStateStoreProvider` provider is an implementation of `System.Web.SessionState.SessionStateStoreProviderBase` that stores the data in hashes in redis.

To use it build the solution. Reference the binaries. Then add this to the configuration file inside the `system.web` element.

```xml 
<sessionState mode="Custom" customProvider="RedisSessionProvider">
	<providers>
		<add name="RedisSessionProvider" type="RedisProviders.RedisSessionStateStoreProvider, RedisProviders" />
	</providers>
</sessionState>
```

See "Option Config Values" for a list of additional options when configuring the provider. For more information on ASP.NET Session State and how to configure it see http://msdn.microsoft.com/en-us/library/ms178581(v=vs.100).aspx

### Redis Role Provider `RedisProviders.RedisRoleProvider`

The `RedisRoleProvider` class is an implementation of `System.Web.Security.RoleProvider` that stores the data in redis.

To use it build the solution. Reference the binaries. Then add this to the configuration file inside the `system.web` element.

```xml
<roleManager defaultProvider="RedisRoleProvider" enabled="true">
	<providers>
		<add name="RedisRoleProvider" type="RedisProviders.RedisRoleProvider" />
	</providers>
</roleManager>
```

You may want to clear the list of providers before adding the Redis Role Provider. See "Option Config Values" for a list of additional options when configuring the provider. For more information on managing ASP.NET Roles see http://msdn.microsoft.com/en-us/library/9ab2fxh0(v=vs.100).aspx

### Redis Membership Provider `RedisProviders.RedisMembershipProvider`

The `RedisMembershipProvider` class is an implementation of `System.Web.Security.MembershipProvider` that stores the data in redis. This provider does not support the ```FindUsersByEmail``` and ```FindUsersByName``` method.

To use it build the solution. Reference the binaries. Then add this to the configuration file inside the `system.web` element.

```xml
<membership defaultProvider="RedisMembershipProvider">
	<providers>
		<add name="RedisMembershipProvider" type="RedisProviders.RedisMembershipProvider" />
	</providers>
</membership>
```

The membership provider has an optional attribute for specifing how to store the password for a user.

* passwordFormat="Hashed" 
* maxInvalidPasswordAttempts="5"
* passwordAttemptWindow="10"
* minRequiredNonAlphanumericCharacters="1"
* minRequiredPasswordLength="7"
* passwordStrengthRegularExpression=""
* enablePasswordReset="true"
* enablePasswordRetrieval="true"
* requiresQuestionAndAnswer="false"
* requiresUniqueEmail="true"

For passwordFormat acceptable values are "Hashed, Encrypted, or Clear." You will need to generate your own machine key for hashed and encrypted password. See http://technet.microsoft.com/en-us/library/cc772287(v=ws.10).aspx for more information. For more information on the ASP.NET Membership Provider and how to configure it see http://msdn.microsoft.com/en-us/library/tw292whz(v=vs.100).aspx

### Redis Web Event Provider `RedisProviders.RedisWebEventProvider`

The `RedisWebEventProvider` class is an implementation of `System.Web.Management.WebEventProvider` that stores the data in redis.

To use it build the solution. Reference the binaries. Then add this to the configuration file inside the `system.web` element.

```xml
<healthMonitoring enabled="true">
	<providers>
		<add name="RedisWebEventProvider" type="RedisProviders.RedisWebEventProvider" />
	</providers>
	<rules>
		<!-- sample rule -->
		<add name="All Web Events"
			eventName="All Events"
			provider="RedisWebEventProvider"
			minInterval="00:00:01" minInstances="1"
			maxLimit="Infinite" />
	</rules>
</healthMonitoring>
```

See "Option Config Values" for a list of additional options when configuring the provider. For more information on managing ASP.NET Health Monitor see http://msdn.microsoft.com/en-us/library/ms178701(v=vs.85).aspx

### Optional Config Values 

There are also some optional attributes for the config for each of the providers and their defaults:

* host="127.0.0.1"
* port="6379"
* password=null
* applicationName=```HostingEnvironment.ApplicationVirtualPath```
* writeExceptionsToEventLog="false"


### TODO:
* Implement other ASP.NET Providers (Site Map, Profile, Web Parts Personalization)




