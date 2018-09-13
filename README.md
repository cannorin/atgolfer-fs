## OpenSSL Issue Workaround

In case of

```
Unhandled Exception: System.Net.WebException: The SSL connection could not be established, see inner exception. Authentication failed, see inner exception. ---> System.Net.Http.HttpRequestException: The SSL connection could not be established, see inner exception. ---> System.Security.Authentication.AuthenticationException: Authentication failed, see inner exception. ---> System.TypeInitializationException: The type initializer for 'SslMethods' threw an exception. ---> System.TypeInitializationException: The type initializer for 'Ssl' threw an exception. ---> System.TypeInitializationException: The type initializer for 'SslInitializer' threw an exception. ---> Interop+Crypto+OpenSslCryptographicException: error:0E076071:configuration file routines:MODULE_RUN:unknown module name
```

add `OPENSSL_CONF=''` before the command.

[Explanation](https://github.com/drwetter/testssl.sh/issues/1117)

## License

Apache 2.
