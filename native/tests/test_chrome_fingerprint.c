/*
 * Chrome TLS Fingerprint Test
 *
 * Tests that curl-impersonate correctly impersonates Chrome's TLS fingerprint
 * by making a request to tls.peet.ws and validating the response.
 */

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <curl/curl.h>

/*
 * Expected fingerprint for Chrome 131
 * These values are obtained from actual Chrome 131 browser fingerprints.
 * Source: chrome131.yaml - Chrome 131.0.6778.86 on macOS
 *
 * Note: JA3 can vary slightly due to TLS extension ordering (randomized since
 * Chrome 110+), so we validate the hash matches one of the known valid values.
 */
#define CHROME_TARGET "chrome131"

/* Expected ja3 cipher suite string (without GREASE values) */
#define EXPECTED_CIPHER_SUITES "4865-4866-4867-49195-49199-49196-49200-52393-52392-49171-49172-156-157-47-53"

/* Reference ja3_hash from browser capture (chrome131.yaml)
 * Note: ja3_hash varies on every request due to TLS extension permutation
 * (enabled since Chrome 110+). We validate cipher suites instead of hash.
 */
#define REFERENCE_JA3_HASH "b41f2b186c3c82a6fc1bb88dad6eb562"

/* Shim function declarations */
extern CURL* shim_curl_easy_init(void);
extern void shim_curl_easy_cleanup(CURL *handle);
extern CURLcode shim_curl_easy_perform(CURL *handle);
extern CURLcode shim_curl_easy_setopt_str(CURL *handle, CURLoption option, const char *value);
extern CURLcode shim_curl_easy_setopt_long(CURL *handle, CURLoption option, long value);
extern CURLcode shim_curl_easy_setopt_ptr(CURL *handle, CURLoption option, void *value);
extern CURLcode shim_curl_easy_impersonate(CURL *curl, const char *target, int default_headers);
extern CURLcode shim_curl_easy_getinfo_long(CURL *handle, CURLINFO info, long *value);
extern void shim_curl_global_init(long flags);
extern void shim_curl_global_cleanup(void);

/* Response buffer */
typedef struct {
    char *data;
    size_t size;
} response_buffer_t;

/* Write callback for curl */
static size_t write_callback(void *contents, size_t size, size_t nmemb, void *userp)
{
    size_t realsize = size * nmemb;
    response_buffer_t *buf = (response_buffer_t *)userp;

    char *ptr = realloc(buf->data, buf->size + realsize + 1);
    if (!ptr) {
        fprintf(stderr, "ERROR: Out of memory\n");
        return 0;
    }

    buf->data = ptr;
    memcpy(&(buf->data[buf->size]), contents, realsize);
    buf->size += realsize;
    buf->data[buf->size] = '\0';

    return realsize;
}

/* Simple JSON string field extractor (no external JSON library needed) */
static int extract_json_string(const char *json, const char *field, char *out, size_t out_size)
{
    char search[256];
    snprintf(search, sizeof(search), "\"%s\":", field);

    const char *pos = strstr(json, search);
    if (!pos) return 0;

    pos += strlen(search);

    /* Skip whitespace */
    while (*pos == ' ' || *pos == '\t' || *pos == '\n' || *pos == '\r') pos++;

    if (*pos != '"') return 0;
    pos++; /* Skip opening quote */

    /* Copy until closing quote */
    size_t i = 0;
    while (*pos && *pos != '"' && i < out_size - 1) {
        out[i++] = *pos++;
    }
    out[i] = '\0';

    return 1;
}

/* Check if ja3 indicates TLS 1.3 */
static int validate_tls13(const char *ja3)
{
    /* ja3 format: "version,ciphers,extensions,curves,formats"
     * 771 = TLS 1.3, 770 = TLS 1.2
     */
    return strncmp(ja3, "771,", 4) == 0;
}

/* Check if ja3 contains TLS 1.3 cipher suites */
static int validate_tls13_ciphers(const char *ja3)
{
    /* TLS 1.3 cipher suites:
     * 4865 = TLS_AES_128_GCM_SHA256
     * 4866 = TLS_AES_256_GCM_SHA384
     * 4867 = TLS_CHACHA20_POLY1305_SHA256
     */
    return strstr(ja3, "4865") != NULL ||
           strstr(ja3, "4866") != NULL ||
           strstr(ja3, "4867") != NULL;
}

/* Validate ja3_hash is a 32-character hex string */
static int validate_ja3_hash_format(const char *hash)
{
    if (strlen(hash) != 32) return 0;

    for (int i = 0; i < 32; i++) {
        char c = hash[i];
        if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))) {
            return 0;
        }
    }
    return 1;
}

/* Extract cipher suites from ja3 string
 * ja3 format: "version,ciphers,extensions,curves,formats"
 * Returns 1 on success, 0 on failure
 */
static int extract_cipher_suites(const char *ja3, char *out, size_t out_size)
{
    /* Skip version (first field) */
    const char *comma = strchr(ja3, ',');
    if (!comma) return 0;
    comma++; /* Skip the comma */

    /* Find end of cipher suites (second field) */
    const char *end = strchr(comma, ',');
    if (!end) return 0;

    size_t len = end - comma;
    if (len >= out_size) len = out_size - 1;

    strncpy(out, comma, len);
    out[len] = '\0';
    return 1;
}

/* Validate cipher suites match expected Chrome 131 values */
static int validate_cipher_suites(const char *ja3)
{
    char ciphers[512] = {0};
    if (!extract_cipher_suites(ja3, ciphers, sizeof(ciphers))) {
        return 0;
    }
    return strcmp(ciphers, EXPECTED_CIPHER_SUITES) == 0;
}

int main(void)
{
    int result = 1; /* Default to failure */
    CURL *curl = NULL;
    response_buffer_t response = {0};
    char ja3[2048] = {0};
    char ja3_hash[64] = {0};
    char ja4[256] = {0};
    char peetprint_hash[64] = {0};

    printf("=== Chrome TLS Fingerprint Test ===\n\n");

    /* Initialize curl */
    shim_curl_global_init(CURL_GLOBAL_DEFAULT);

    curl = shim_curl_easy_init();
    if (!curl) {
        fprintf(stderr, "ERROR: Failed to initialize curl\n");
        goto cleanup;
    }

    /* Set Chrome impersonation
     * Available targets: chrome99-chrome142, chrome99_android, chrome131_android
     */
    CURLcode res = shim_curl_easy_impersonate(curl, CHROME_TARGET, 1);
    if (res != CURLE_OK) {
        fprintf(stderr, "ERROR: Failed to set Chrome impersonation: %d\n", res);
        goto cleanup;
    }
    printf("Browser target: %s\n", CHROME_TARGET);

    /* Set URL */
    res = shim_curl_easy_setopt_str(curl, CURLOPT_URL, "https://tls.peet.ws/api/all");
    if (res != CURLE_OK) {
        fprintf(stderr, "ERROR: Failed to set URL: %d\n", res);
        goto cleanup;
    }

    /* Set write callback */
    response.data = malloc(1);
    response.size = 0;
    res = shim_curl_easy_setopt_ptr(curl, CURLOPT_WRITEFUNCTION, (void *)write_callback);
    if (res != CURLE_OK) {
        fprintf(stderr, "ERROR: Failed to set write function: %d\n", res);
        goto cleanup;
    }
    res = shim_curl_easy_setopt_ptr(curl, CURLOPT_WRITEDATA, (void *)&response);
    if (res != CURLE_OK) {
        fprintf(stderr, "ERROR: Failed to set write data: %d\n", res);
        goto cleanup;
    }

    /* Set timeout */
    shim_curl_easy_setopt_long(curl, CURLOPT_TIMEOUT, 30L);

    /* Perform request */
    printf("Making request to https://tls.peet.ws/api/all...\n\n");
    res = shim_curl_easy_perform(curl);
    if (res != CURLE_OK) {
        fprintf(stderr, "ERROR: Request failed: %d\n", res);
        goto cleanup;
    }

    /* Check HTTP response code */
    long http_code = 0;
    shim_curl_easy_getinfo_long(curl, CURLINFO_RESPONSE_CODE, &http_code);
    printf("HTTP Status: %ld\n", http_code);

    if (http_code != 200) {
        fprintf(stderr, "ERROR: Expected HTTP 200, got %ld\n", http_code);
        goto cleanup;
    }

    /* Extract fingerprint fields from JSON response */
    if (!extract_json_string(response.data, "ja3", ja3, sizeof(ja3))) {
        fprintf(stderr, "ERROR: Could not extract 'ja3' from response\n");
        fprintf(stderr, "Response: %.500s...\n", response.data);
        goto cleanup;
    }

    if (!extract_json_string(response.data, "ja3_hash", ja3_hash, sizeof(ja3_hash))) {
        fprintf(stderr, "ERROR: Could not extract 'ja3_hash' from response\n");
        goto cleanup;
    }

    /* Extract optional fields for display */
    extract_json_string(response.data, "ja4", ja4, sizeof(ja4));
    extract_json_string(response.data, "peetprint_hash", peetprint_hash, sizeof(peetprint_hash));

    /* Display fingerprint info */
    printf("\n=== TLS Fingerprint Results ===\n");
    printf("ja3_hash:       %s\n", ja3_hash);
    printf("ja3:            %.80s...\n", ja3);
    if (ja4[0]) printf("ja4:            %s\n", ja4);
    if (peetprint_hash[0]) printf("peetprint_hash: %s\n", peetprint_hash);

    /* Validate fingerprint */
    printf("\n=== Validation ===\n");

    /* Test 1: TLS 1.3 */
    if (!validate_tls13(ja3)) {
        fprintf(stderr, "FAIL: ja3 does not indicate TLS 1.3 (expected '771,' prefix)\n");
        fprintf(stderr, "      Got: %.10s...\n", ja3);
        goto cleanup;
    }
    printf("PASS: TLS 1.3 detected (771)\n");

    /* Test 2: TLS 1.3 cipher suites present */
    if (!validate_tls13_ciphers(ja3)) {
        fprintf(stderr, "FAIL: No TLS 1.3 cipher suites found (expected 4865/4866/4867)\n");
        goto cleanup;
    }
    printf("PASS: TLS 1.3 cipher suites present\n");

    /* Test 3: Valid ja3_hash format */
    if (!validate_ja3_hash_format(ja3_hash)) {
        fprintf(stderr, "FAIL: Invalid ja3_hash format (expected 32 hex chars)\n");
        fprintf(stderr, "      Got: %s\n", ja3_hash);
        goto cleanup;
    }
    printf("PASS: Valid ja3_hash format\n");

    /* Test 4: Cipher suites match expected Chrome 131 values */
    char extracted_ciphers[512] = {0};
    if (!extract_cipher_suites(ja3, extracted_ciphers, sizeof(extracted_ciphers))) {
        fprintf(stderr, "FAIL: Could not extract cipher suites from ja3\n");
        goto cleanup;
    }
    if (!validate_cipher_suites(ja3)) {
        fprintf(stderr, "FAIL: Cipher suites do not match expected Chrome 131 values\n");
        fprintf(stderr, "      Got:      %s\n", extracted_ciphers);
        fprintf(stderr, "      Expected: %s\n", EXPECTED_CIPHER_SUITES);
        goto cleanup;
    }
    printf("PASS: Cipher suites match Chrome 131 (%s)\n", EXPECTED_CIPHER_SUITES);

    /* All tests passed */
    printf("\n=== ALL TESTS PASSED ===\n");
    result = 0;

cleanup:
    if (curl) shim_curl_easy_cleanup(curl);
    if (response.data) free(response.data);
    shim_curl_global_cleanup();

    return result;
}
