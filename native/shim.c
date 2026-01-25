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
 * Wrap curl_easy_setopt for blob (struct curl_blob*) parameter type
 * Used for: CURLOPT_SSLCERT_BLOB, CURLOPT_SSLKEY_BLOB, CURLOPT_CAINFO_BLOB, etc.
 */
SHIM_EXPORT CURLcode shim_curl_easy_setopt_blob(CURL *handle, CURLoption option, struct curl_blob *blob)
{
    return curl_easy_setopt(handle, option, blob);
}

/*
 * Callback setters - wrap curl_easy_setopt for function pointer options
 * These use typedefs from curl/curl.h
 */

/* Set write callback for response body (CURLOPT_WRITEFUNCTION) */
SHIM_EXPORT CURLcode shim_curl_easy_setopt_write_cb(CURL *handle, curl_write_callback callback)
{
    return curl_easy_setopt(handle, CURLOPT_WRITEFUNCTION, callback);
}

/* Set header callback (CURLOPT_HEADERFUNCTION) - same signature as write */
SHIM_EXPORT CURLcode shim_curl_easy_setopt_header_cb(CURL *handle, curl_write_callback callback)
{
    return curl_easy_setopt(handle, CURLOPT_HEADERFUNCTION, callback);
}

/* Set read callback for request body (CURLOPT_READFUNCTION) */
SHIM_EXPORT CURLcode shim_curl_easy_setopt_read_cb(CURL *handle, curl_read_callback callback)
{
    return curl_easy_setopt(handle, CURLOPT_READFUNCTION, callback);
}

/* Set transfer progress callback (CURLOPT_XFERINFOFUNCTION) */
SHIM_EXPORT CURLcode shim_curl_easy_setopt_xferinfo_cb(CURL *handle, curl_xferinfo_callback callback)
{
    return curl_easy_setopt(handle, CURLOPT_XFERINFOFUNCTION, callback);
}

/* Set debug/verbose callback (CURLOPT_DEBUGFUNCTION) */
SHIM_EXPORT CURLcode shim_curl_easy_setopt_debug_cb(CURL *handle, curl_debug_callback callback)
{
    return curl_easy_setopt(handle, CURLOPT_DEBUGFUNCTION, callback);
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

/*
 * Get info as curl_off_t (64-bit integer)
 * Used for: CURLINFO_SIZE_UPLOAD_T, CURLINFO_SIZE_DOWNLOAD_T,
 *           CURLINFO_TOTAL_TIME_T, CURLINFO_SPEED_DOWNLOAD_T, etc.
 */
SHIM_EXPORT CURLcode shim_curl_easy_getinfo_off_t(CURL *handle, CURLINFO info, curl_off_t *value)
{
    return curl_easy_getinfo(handle, info, value);
}

/*
 * Get info as slist (linked list)
 * Used for: CURLINFO_SSL_ENGINES, CURLINFO_COOKIELIST
 * Note: Caller must free with curl_slist_free_all()
 */
SHIM_EXPORT CURLcode shim_curl_easy_getinfo_slist(CURL *handle, CURLINFO info, struct curl_slist **value)
{
    return curl_easy_getinfo(handle, info, value);
}

SHIM_EXPORT void shim_curl_easy_reset(CURL *handle)
{
    curl_easy_reset(handle);
}

/* Duplicate an easy handle with all its options */
SHIM_EXPORT CURL* shim_curl_easy_duphandle(CURL *handle)
{
    return curl_easy_duphandle(handle);
}

/* Get libcurl version string (returns pointer to static string) */
SHIM_EXPORT const char* shim_curl_version(void)
{
    return curl_version();
}

/* ==========================================================================
 * Multi interface
 * ========================================================================== */

/* Lifecycle */
SHIM_EXPORT CURLM* shim_curl_multi_init(void)
{
    return curl_multi_init();
}

SHIM_EXPORT CURLMcode shim_curl_multi_cleanup(CURLM *multi)
{
    return curl_multi_cleanup(multi);
}

/* Handle management */
SHIM_EXPORT CURLMcode shim_curl_multi_add_handle(CURLM *multi, CURL *easy)
{
    return curl_multi_add_handle(multi, easy);
}

SHIM_EXPORT CURLMcode shim_curl_multi_remove_handle(CURLM *multi, CURL *easy)
{
    return curl_multi_remove_handle(multi, easy);
}

/* Driving transfers */
SHIM_EXPORT CURLMcode shim_curl_multi_perform(CURLM *multi, int *running_handles)
{
    return curl_multi_perform(multi, running_handles);
}

SHIM_EXPORT CURLMcode shim_curl_multi_socket_action(CURLM *multi, curl_socket_t sockfd,
                                                     int ev_bitmask, int *running_handles)
{
    return curl_multi_socket_action(multi, sockfd, ev_bitmask, running_handles);
}

SHIM_EXPORT CURLMcode shim_curl_multi_poll(CURLM *multi, struct curl_waitfd *extra_fds,
                                            unsigned int extra_nfds, int timeout_ms, int *numfds)
{
    return curl_multi_poll(multi, extra_fds, extra_nfds, timeout_ms, numfds);
}

SHIM_EXPORT CURLMcode shim_curl_multi_wakeup(CURLM *multi)
{
    return curl_multi_wakeup(multi);
}

/* Getting results */
SHIM_EXPORT CURLMsg* shim_curl_multi_info_read(CURLM *multi, int *msgs_in_queue)
{
    return curl_multi_info_read(multi, msgs_in_queue);
}

/* Error handling */
SHIM_EXPORT const char* shim_curl_multi_strerror(CURLMcode code)
{
    return curl_multi_strerror(code);
}

/* Setopt variants */
SHIM_EXPORT CURLMcode shim_curl_multi_setopt_long(CURLM *multi, CURLMoption option, long value)
{
    return curl_multi_setopt(multi, option, value);
}

SHIM_EXPORT CURLMcode shim_curl_multi_setopt_ptr(CURLM *multi, CURLMoption option, void *value)
{
    return curl_multi_setopt(multi, option, value);
}

/* Callback setters */
SHIM_EXPORT CURLMcode shim_curl_multi_setopt_socket_cb(CURLM *multi, curl_socket_callback callback)
{
    return curl_multi_setopt(multi, CURLMOPT_SOCKETFUNCTION, callback);
}

SHIM_EXPORT CURLMcode shim_curl_multi_setopt_timer_cb(CURLM *multi, curl_multi_timer_callback callback)
{
    return curl_multi_setopt(multi, CURLMOPT_TIMERFUNCTION, callback);
}
