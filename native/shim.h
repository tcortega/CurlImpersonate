#include <stdint.h>
#include <curl/curl.h>

int shim_easy_setopt(void* curl, int option, void* param);
int shim_easy_getinfo(void* curl, int info, void* param);
