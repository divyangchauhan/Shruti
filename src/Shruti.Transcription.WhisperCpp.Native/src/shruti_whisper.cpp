#include "shruti_whisper.h"

#include "ggml-backend.h"
#include "whisper.h"

#include <new>

struct shruti_whisper_context {
    whisper_context * native_context;
};

struct shruti_whisper_abort_state {
    shruti_whisper_abort_callback callback;
    void * user_data;
};

static bool shruti_whisper_should_abort(void * user_data) {
    auto * state = static_cast<shruti_whisper_abort_state *>(user_data);
    return state != nullptr && state->callback != nullptr && state->callback(state->user_data) != 0;
}

static bool shruti_whisper_has_gpu_device(void) {
#if defined(SHRUTI_WHISPER_GPU_ENABLED)
    ggml_backend_load_all();
    for (size_t index = 0; index < ggml_backend_dev_count(); ++index) {
        ggml_backend_dev_t device = ggml_backend_dev_get(index);
        const enum ggml_backend_dev_type type = ggml_backend_dev_type(device);
        if (type == GGML_BACKEND_DEVICE_TYPE_GPU || type == GGML_BACKEND_DEVICE_TYPE_IGPU) {
            return true;
        }
    }
#endif

    return false;
}

int shruti_whisper_available_backends(void) {
    int flags = SHRUTI_WHISPER_BACKEND_FLAG_CPU;
    if (shruti_whisper_has_gpu_device()) {
        flags |= SHRUTI_WHISPER_BACKEND_FLAG_GPU;
    }

    return flags;
}

shruti_whisper_context * shruti_whisper_create(const char * model_path) {
    return shruti_whisper_create_with_backend(model_path, SHRUTI_WHISPER_BACKEND_CPU, 0);
}

shruti_whisper_context * shruti_whisper_create_with_backend(const char * model_path, int backend, int gpu_device) {
    if (model_path == nullptr || model_path[0] == '\0') {
        return nullptr;
    }

    const bool use_gpu = backend == SHRUTI_WHISPER_BACKEND_GPU;
    if (backend != SHRUTI_WHISPER_BACKEND_CPU && backend != SHRUTI_WHISPER_BACKEND_GPU) {
        return nullptr;
    }

    if (use_gpu && !shruti_whisper_has_gpu_device()) {
        return nullptr;
    }

    try {
        whisper_context_params parameters = whisper_context_default_params();
        parameters.use_gpu = use_gpu;
        parameters.gpu_device = gpu_device < 0 ? 0 : gpu_device;
        parameters.flash_attn = false;
        whisper_context * native_context = whisper_init_from_file_with_params(model_path, parameters);
        if (native_context == nullptr) {
            return nullptr;
        }

        return new shruti_whisper_context { native_context };
    } catch (...) {
        return nullptr;
    }
}

void shruti_whisper_free(shruti_whisper_context * context) {
    if (context == nullptr) {
        return;
    }

    whisper_free(context->native_context);
    delete context;
}

int shruti_whisper_transcribe(
    shruti_whisper_context * context,
    const float * samples,
    int sample_count,
    const char * language,
    int thread_count,
    shruti_whisper_abort_callback abort_callback,
    void * abort_callback_user_data) {
    if (context == nullptr || context->native_context == nullptr || samples == nullptr || sample_count <= 0 ||
        thread_count <= 0) {
        return -1;
    }

    try {
        whisper_full_params parameters = whisper_full_default_params(WHISPER_SAMPLING_GREEDY);
        shruti_whisper_abort_state abort_state { abort_callback, abort_callback_user_data };
        parameters.n_threads = thread_count;
        parameters.abort_callback = abort_callback == nullptr ? nullptr : shruti_whisper_should_abort;
        parameters.abort_callback_user_data = abort_callback == nullptr ? nullptr : &abort_state;
        parameters.language = language != nullptr && language[0] != '\0' ? language : "en";
        parameters.print_special = false;
        parameters.print_progress = false;
        parameters.print_realtime = false;
        parameters.print_timestamps = false;

        return whisper_full(context->native_context, parameters, samples, sample_count);
    } catch (...) {
        return -2;
    }
}

int shruti_whisper_get_segment_count(const shruti_whisper_context * context) {
    if (context == nullptr || context->native_context == nullptr) {
        return -1;
    }

    return whisper_full_n_segments(context->native_context);
}

int shruti_whisper_get_segment(
    const shruti_whisper_context * context,
    int index,
    int64_t * start_milliseconds,
    int64_t * end_milliseconds,
    const char ** text) {
    if (context == nullptr || context->native_context == nullptr || index < 0 || start_milliseconds == nullptr ||
        end_milliseconds == nullptr || text == nullptr) {
        return -1;
    }

    const int count = whisper_full_n_segments(context->native_context);
    if (index >= count) {
        return -1;
    }

    *start_milliseconds = whisper_full_get_segment_t0(context->native_context, index) * 10;
    *end_milliseconds = whisper_full_get_segment_t1(context->native_context, index) * 10;
    *text = whisper_full_get_segment_text(context->native_context, index);
    return 0;
}

const char * shruti_whisper_system_info(void) {
    return whisper_print_system_info();
}
