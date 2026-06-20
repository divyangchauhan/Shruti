#pragma once

#include <stdint.h>

#if defined(_WIN32)
#if defined(SHRUTI_WHISPER_BUILD)
#define SHRUTI_WHISPER_API __declspec(dllexport)
#else
#define SHRUTI_WHISPER_API __declspec(dllimport)
#endif
#else
#define SHRUTI_WHISPER_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef struct shruti_whisper_context shruti_whisper_context;

SHRUTI_WHISPER_API shruti_whisper_context * shruti_whisper_create(const char * model_path);
SHRUTI_WHISPER_API void shruti_whisper_free(shruti_whisper_context * context);

SHRUTI_WHISPER_API int shruti_whisper_transcribe(
    shruti_whisper_context * context,
    const float * samples,
    int sample_count,
    const char * language,
    int thread_count);

SHRUTI_WHISPER_API int shruti_whisper_get_segment_count(const shruti_whisper_context * context);
SHRUTI_WHISPER_API int shruti_whisper_get_segment(
    const shruti_whisper_context * context,
    int index,
    int64_t * start_milliseconds,
    int64_t * end_milliseconds,
    const char ** text);

SHRUTI_WHISPER_API const char * shruti_whisper_system_info(void);

#ifdef __cplusplus
}
#endif
