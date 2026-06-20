#include "shruti_whisper.h"

int main() {
    const char * system_info = shruti_whisper_system_info();
    return system_info != nullptr && system_info[0] != '\0' ? 0 : 1;
}
