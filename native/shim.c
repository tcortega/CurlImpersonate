/*
 * curl-impersonate shim layer
 * Wraps variadic curl_easy_setopt for ABI compatibility with .NET P/Invoke
 */

#include <curl/curl.h>

/* Export macro for cross-platform shared library */
#ifdef _WIN32
    #define SHIM_EXPORT __declspec(dllexport)
#else
    #define SHIM_EXPORT __attribute__((visibility("default")))
#endif

/*
 * Wrap curl_easy_setopt for long parameter type
 * Used for: CURLOPT_VERBOSE, CURLOPT_FOLLOWLOCATION, CURLOPT_TIMEOUT, etc.
 */
SHIM_EXPORT CURLcode shim_curl_easy_setopt_long(CURL *handle, CURLoption option, long value)
{
    return curl_easy_setopt(handle, option, value);
}

/*
 * Wrap curl_easy_setopt for string (const char*) parameter type
 * Used for: CURLOPT_URL, CURLOPT_USERAGENT, CURLOPT_COOKIE, etc.
 */
SHIM_EXPORT CURLcode shim_curl_easy_setopt_str(CURL *handle, CURLoption option, const char *value)
{
    return curl_easy_setopt(handle, option, value);
}

/*
 * Wrap curl_easy_setopt for pointer (void*) parameter type
 * Used for: CURLOPT_WRITEDATA, CURLOPT_READDATA, CURLOPT_HTTPHEADER, etc.
 */
SHIM_EXPORT CURLcode shim_curl_easy_setopt_ptr(CURL *handle, CURLoption option, void *value)
{
    return curl_easy_setopt(handle, option, value);
}

/*
 * Wrap curl_easy_setopt for curl_off_t parameter type
 * Used for: CURLOPT_POSTFIELDSIZE_LARGE, CURLOPT_INFILESIZE_LARGE, etc.
 */
SHIM_EXPORT CURLcode shim_curl_easy_setopt_off_t(CURL *handle, CURLoption option, curl_off_t value)
{
    return curl_easy_setopt(handle, option, value);
}

/*
 * Forward curl_easy_impersonate
 * Configures the CURL handle to impersonate a specific browser's TLS fingerprint
 *
 * @param curl           The CURL easy handle
 * @param target         Browser target string (e.g., "chrome", "chrome124", "safari17_0")
 * @param default_headers Non-zero to apply browser's default HTTP headers
 * @return CURLcode      CURLE_OK on success
 */
SHIM_EXPORT CURLcode shim_curl_easy_impersonate(CURL *curl, const char *target, int default_headers)
{
    return curl_easy_impersonate(curl, target, default_headers);
}

/*
 * Forward common curl functions that may need explicit export
 */
SHIM_EXPORT CURL* shim_curl_easy_init(void)
{
    return curl_easy_init();
}

SHIM_EXPORT void shim_curl_easy_cleanup(CURL *handle)
{
    curl_easy_cleanup(handle);
}

SHIM_EXPORT CURLcode shim_curl_easy_perform(CURL *handle)
{
    return curl_easy_perform(handle);
}

SHIM_EXPORT void shim_curl_global_init(long flags)
{
    curl_global_init(flags);
}

SHIM_EXPORT void shim_curl_global_cleanup(void)
{
    curl_global_cleanup();
}

SHIM_EXPORT const char* shim_curl_easy_strerror(CURLcode code)
{
    return curl_easy_strerror(code);
}

SHIM_EXPORT struct curl_slist* shim_curl_slist_append(struct curl_slist *list, const char *string)
{
    return curl_slist_append(list, string);
}

SHIM_EXPORT void shim_curl_slist_free_all(struct curl_slist *list)
{
    curl_slist_free_all(list);
}

SHIM_EXPORT CURLcode shim_curl_easy_getinfo_long(CURL *handle, CURLINFO info, long *value)
{
    return curl_easy_getinfo(handle, info, value);
}

SHIM_EXPORT CURLcode shim_curl_easy_getinfo_str(CURL *handle, CURLINFO info, char **value)
{
    return curl_easy_getinfo(handle, info, value);
}

SHIM_EXPORT CURLcode shim_curl_easy_getinfo_double(CURL *handle, CURLINFO info, double *value)
{
    return curl_easy_getinfo(handle, info, value);
}

SHIM_EXPORT void shim_curl_easy_reset(CURL *handle)
{
    curl_easy_reset(handle);
}
