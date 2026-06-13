#include <stdint.h>
#include <curl/curl.h>

#if defined(_WIN32)
#define SHIM_API __declspec(dllexport)
#else
#define SHIM_API __attribute__((visibility("default")))
#endif

SHIM_API int shim_global_init(int64_t flags);
SHIM_API int shim_easy_setopt_long(void* curl, int option, int64_t value);
SHIM_API int shim_easy_setopt_off_t(void* curl, int option, int64_t value);
SHIM_API int shim_easy_setopt_ptr(void* curl, int option, void* param);
SHIM_API int shim_easy_getinfo_ptr(void* curl, int info, void** value);
SHIM_API int shim_easy_getinfo_long(void* curl, int info, int64_t* value);
SHIM_API int shim_easy_getinfo_off_t(void* curl, int info, int64_t* value);
SHIM_API int shim_easy_getinfo_double(void* curl, int info, double* value);
SHIM_API int shim_multi_setopt_long(void* multi, int option, int64_t value);
