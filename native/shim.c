#include "shim.h"

int shim_easy_setopt(void* curl, int option, void* param) {
    if (option < CURLOPTTYPE_OBJECTPOINT) {
        return (int)curl_easy_setopt(curl, (CURLoption)option, (long)(*(int64_t*)param));
    }
    if (option >= CURLOPTTYPE_OFF_T && option < CURLOPTTYPE_BLOB) {
        return (int)curl_easy_setopt(curl, (CURLoption)option, *(curl_off_t*)param);
    }
    return (int)curl_easy_setopt(curl, (CURLoption)option, param);
}

int shim_easy_getinfo(void* curl, int info, void* param) {
    // For curl_easy_getinfo, all info types expect a pointer that curl writes to.
    // We simply pass the pointer through to the variadic function.
    return (int)curl_easy_getinfo(curl, (CURLINFO)info, param);
}
