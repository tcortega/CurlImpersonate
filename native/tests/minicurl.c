/*
 * minicurl - A minimal curl application for testing curl-impersonate shim
 *
 * This program uses the curl-shim library to make HTTP requests with browser
 * impersonation. It reads the CURL_IMPERSONATE environment variable to determine
 * which browser to impersonate.
 *
 * Based on curl-impersonate/tests/minicurl.c but adapted to use shim functions.
 */
#include <stdio.h>
#include <stdlib.h>
#include <stdint.h>
#include <string.h>
#include <errno.h>
#include <getopt.h>
#include <stdbool.h>

#include <curl/curl.h>

/* Shim function declarations */
extern CURL* shim_curl_easy_init(void);
extern void shim_curl_easy_cleanup(CURL *handle);
extern CURLcode shim_curl_easy_perform(CURL *handle);
extern CURLcode shim_curl_easy_setopt_str(CURL *handle, CURLoption option, const char *value);
extern CURLcode shim_curl_easy_setopt_long(CURL *handle, CURLoption option, long value);
extern CURLcode shim_curl_easy_setopt_ptr(CURL *handle, CURLoption option, void *value);
extern CURLcode shim_curl_easy_impersonate(CURL *curl, const char *target, int default_headers);
extern void shim_curl_global_init(long flags);
extern void shim_curl_global_cleanup(void);
extern const char* shim_curl_easy_strerror(CURLcode error);
extern struct curl_slist* shim_curl_slist_append(struct curl_slist *list, const char *string);
extern void shim_curl_slist_free_all(struct curl_slist *list);
extern void shim_curl_easy_reset(CURL *handle);

/* Support up to 16 URLs */
#define MAX_URLS 16

/* Command line options. */
struct opts {
    char *outfile;
    uint16_t local_port_start;
    uint16_t local_port_end;
    bool insecure;
    char *urls[MAX_URLS];
    char *user_agent;
    struct curl_slist *headers;
};

int parse_ports_range(char *str, uint16_t *start, uint16_t *end)
{
    char port[32];
    char *sep;
    unsigned long int tmp;

    if (strlen(str) >= sizeof(port)) {
        return 1;
    }
    strncpy(port, str, sizeof(port) - 1);
    port[sizeof(port) - 1] = '\0';
    sep = strchr(port, '-');
    if (!sep) {
        return 1;
    }
    *sep = 0;

    errno = 0;
    tmp = strtoul(port, NULL, 10);
    if (errno || tmp == 0 || tmp > 0xffff) {
        return 1;
    }
    *start = (uint16_t)tmp;
    tmp = strtoul(sep + 1, NULL, 10);
    if (errno || tmp == 0 || tmp > 0xffff || tmp < *start) {
        return 1;
    }
    *end = (uint16_t)tmp;

    return 0;
}

int parse_opts(int argc, char **argv, struct opts *opts)
{
    int c;
    int r;
    int i;
    struct curl_slist *tmp;

    memset(opts, 0, sizeof(*opts));

    opts->insecure = false;

    while (1) {
        int option_index = 0;
        static struct option long_options[] = {
            {"header", required_argument, NULL, 'H'},
            {"local-port", required_argument, NULL, 'l'},
            {"user-agent", required_argument, NULL, 'A'},
            {0, 0, NULL, 0}
        };

        c = getopt_long(argc, argv, "o:kH:A:", long_options, &option_index);
        if (c == -1) {
            break;
        }

        switch (c) {
        case 'A':
            opts->user_agent = optarg;
            break;
        case 'l':
            r = parse_ports_range(optarg,
                                  &opts->local_port_start,
                                  &opts->local_port_end);
            if (r) {
                return r;
            }
            break;
        case 'o':
            opts->outfile = optarg;
            break;
        case 'k':
            opts->insecure = true;
            break;
        case 'H':
            tmp = shim_curl_slist_append(opts->headers, optarg);
            if (!tmp) {
                fprintf(stderr, "curl_slist_append() failed\n");
                if (opts->headers) {
                    shim_curl_slist_free_all(opts->headers);
                }
                return 1;
            }
            opts->headers = tmp;
            break;
        case '?':
            break;
        }
    }

    /* No URL supplied. */
    i = 0;
    if (optind >= argc) {
        return 1;
    }

    /* The rest of the options are URLs */
    while (optind < argc && i < MAX_URLS) {
        opts->urls[i++] = argv[optind++];
    }

    return 0;
}

void clean_opts(struct opts *opts)
{
    if (opts->headers) {
        shim_curl_slist_free_all(opts->headers);
    }
}

/* Set all options except for the URL. */
int set_opts(CURL *curl, struct opts *opts, FILE *file)
{
    CURLcode c;

    c = shim_curl_easy_setopt_ptr(curl, CURLOPT_WRITEFUNCTION, (void *)fwrite);
    if (c) {
        fprintf(stderr, "curl_easy_setopt(CURLOPT_WRITEFUNCTION) failed\n");
        return 1;
    }

    c = shim_curl_easy_setopt_ptr(curl, CURLOPT_WRITEDATA, (void *)file);
    if (c) {
        fprintf(stderr, "curl_easy_setopt(CURLOPT_WRITEDATA) failed\n");
        return 1;
    }

    if (opts->local_port_start && opts->local_port_end) {
        c = shim_curl_easy_setopt_long(curl,
                             CURLOPT_LOCALPORT,
                             opts->local_port_start);
        if (c) {
            fprintf(stderr, "curl_easy_setopt(CURLOPT_LOCALPORT) failed\n");
            return 1;
        }

        c = shim_curl_easy_setopt_long(curl,
                             CURLOPT_LOCALPORTRANGE,
                             opts->local_port_end - opts->local_port_start);
        if (c) {
            fprintf(stderr,
                    "curl_easy_setopt(CURLOPT_LOCALPORTRANGE) failed\n");
            return 1;
        }
    }

    if (opts->insecure) {
        c = shim_curl_easy_setopt_long(curl, CURLOPT_SSL_VERIFYPEER, 0);
        if (c) {
            fprintf(stderr, "curl_easy_setopt(CURLOPT_SSL_VERIFYPEER) failed\n");
            return 1;
        }
        c = shim_curl_easy_setopt_long(curl, CURLOPT_SSL_VERIFYHOST, 0);
        if (c) {
            fprintf(stderr, "curl_easy_setopt(CURLOPT_SSL_VERIFYHOST) failed\n");
            return 1;
        }
    }

    if (opts->user_agent) {
        c = shim_curl_easy_setopt_str(curl, CURLOPT_USERAGENT, opts->user_agent);
        if (c) {
            fprintf(stderr, "curl_easy_setopt(CURLOPT_USERAGENT) failed\n");
            return 1;
        }
    }

    if (opts->headers) {
        c = shim_curl_easy_setopt_ptr(curl, CURLOPT_HTTPHEADER, (void *)opts->headers);
        if (c) {
            fprintf(stderr, "curl_easy_setopt(CURLOPT_HTTPHEADER) failed\n");
            return 1;
        }
    }

    return 0;
}

int main(int argc, char *argv[])
{
    struct opts opts;
    CURLcode c;
    CURL *curl = NULL;
    FILE *file;
    int i;
    const char *impersonate_target;
    int use_default_headers = 1;

    if (parse_opts(argc, argv, &opts)) {
        fprintf(stderr, "Invalid arguments\n");
        exit(1);
    }

    if (opts.outfile) {
        file = fopen(opts.outfile, "w");
        if (!file) {
            fprintf(stderr, "Failed opening %s for writing\n", opts.outfile);
            c = 1;
            goto out_clean_opts;
        }
    } else {
        file = stdout;
    }

    shim_curl_global_init(CURL_GLOBAL_DEFAULT);

    curl = shim_curl_easy_init();
    if (!curl) {
        fprintf(stderr, "curl_easy_init() failed\n");
        c = 1;
        goto out;
    }

    /* Check for impersonation target from environment */
    impersonate_target = getenv("CURL_IMPERSONATE");
    if (impersonate_target) {
        /* Check if default headers should be disabled */
        const char *headers_env = getenv("CURL_IMPERSONATE_HEADERS");
        if (headers_env && strcmp(headers_env, "no") == 0) {
            use_default_headers = 0;
        }

        c = shim_curl_easy_impersonate(curl, impersonate_target, use_default_headers);
        if (c) {
            fprintf(stderr, "curl_easy_impersonate(%s) failed: %d\n",
                    impersonate_target, c);
            goto out;
        }
    }

    for (i = 0; i < MAX_URLS && opts.urls[i]; i++) {
        if (set_opts(curl, &opts, file)) {
            goto out;
        }

        c = shim_curl_easy_setopt_str(curl, CURLOPT_URL, opts.urls[i]);
        if (c) {
            fprintf(stderr, "curl_easy_setopt(CURLOPT_URL) failed\n");
            goto out;
        }

        c = shim_curl_easy_perform(curl);
        if (c) {
            fprintf(stderr,
                    "curl_easy_perform() failed: %d (%s)\n",
                    c, shim_curl_easy_strerror(c));
            goto out;
        }

        /* Re-use the curl handle - need to re-apply impersonation after reset */
        shim_curl_easy_reset(curl);
        if (impersonate_target) {
            shim_curl_easy_impersonate(curl, impersonate_target, use_default_headers);
        }
    }

    c = 0;

out:
    if (curl) {
        shim_curl_easy_cleanup(curl);
    }
    shim_curl_global_cleanup();
    if (file && file != stdout) {
        fclose(file);
    }
out_clean_opts:
    clean_opts(&opts);
    return c;
}
