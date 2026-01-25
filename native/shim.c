#include "shim.h"

int shim_easy_setopt(void* curl, int option, void* param) {
    if (option < CURLOPTTYPE_OBJECTPOINT) {
        return (int)curl_easy_setopt(curl, (CURLoption)option, *(long*)param);
    }
    if (option >= CURLOPTTYPE_OFF_T && option < CURLOPTTYPE_BLOB) {
        return (int)curl_easy_setopt(curl, (CURLoption)option, *(curl_off_t*)param);
    }
    return (int)curl_easy_setopt(curl, (CURLoption)option, param);
}
