#include "shruti_whisper.h"

int main() {
    const char * system_info = shruti_whisper_system_info();
    // Exercise the versioned ABI without loading a model. Invalid input must fail safely.
    if (shruti_whisper_transcribe_with_prompt(nullptr, nullptr, 0, "en", 1, "Shruti", nullptr, nullptr) != -1) {
        return 2;
    }
    return system_info != nullptr && system_info[0] != '\0' ? 0 : 1;
}
