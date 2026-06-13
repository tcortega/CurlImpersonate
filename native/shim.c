#include "shim.h"
#include <limits.h>

static int shim_long_in_range(int64_t value) {
    return value >= LONG_MIN && value <= LONG_MAX;
}

int shim_global_init(int64_t flags) {
    if (!shim_long_in_range(flags)) {
        return (int)CURLE_BAD_FUNCTION_ARGUMENT;
    }

    return (int)curl_global_init((long)flags);
}

int shim_easy_setopt_long(void* curl, int option, int64_t value) {
    if (!shim_long_in_range(value)) {
        return (int)CURLE_BAD_FUNCTION_ARGUMENT;
    }

    return (int)curl_easy_setopt(curl, (CURLoption)option, (long)value);
}

int shim_easy_setopt_off_t(void* curl, int option, int64_t value) {
    return (int)curl_easy_setopt(curl, (CURLoption)option, (curl_off_t)value);
}

int shim_easy_setopt_ptr(void* curl, int option, void* param) {
    return (int)curl_easy_setopt(curl, (CURLoption)option, param);
}

int shim_easy_getinfo_ptr(void* curl, int info, void** value) {
    return (int)curl_easy_getinfo(curl, (CURLINFO)info, value);
}

int shim_easy_getinfo_long(void* curl, int info, int64_t* value) {
    long native_value = 0;
    CURLcode code = curl_easy_getinfo(curl, (CURLINFO)info, &native_value);
    if (code == CURLE_OK) {
        *value = (int64_t)native_value;
    }
    return (int)code;
}

int shim_easy_getinfo_off_t(void* curl, int info, int64_t* value) {
    curl_off_t native_value = 0;
    CURLcode code = curl_easy_getinfo(curl, (CURLINFO)info, &native_value);
    if (code == CURLE_OK) {
        *value = (int64_t)native_value;
    }
    return (int)code;
}

int shim_easy_getinfo_double(void* curl, int info, double* value) {
    return (int)curl_easy_getinfo(curl, (CURLINFO)info, value);
}

int shim_multi_setopt_long(void* multi, int option, int64_t value) {
    if (!shim_long_in_range(value)) {
        return (int)CURLM_BAD_FUNCTION_ARGUMENT;
    }

    return (int)curl_multi_setopt(multi, (CURLMoption)option, (long)value);
}
