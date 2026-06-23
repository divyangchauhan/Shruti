cmake_minimum_required(VERSION 3.21)

project(ShrutiWhisperCppNative LANGUAGES C CXX)

include(FetchContent)
include(CTest)

set(BUILD_SHARED_LIBS OFF CACHE BOOL "Build whisper.cpp dependencies as static libraries" FORCE)
set(GGML_NATIVE OFF CACHE BOOL "Avoid compiling host-specific instructions into the distributed CPU build" FORCE)
set(WHISPER_BUILD_TESTS OFF CACHE BOOL "Disable upstream whisper.cpp tests" FORCE)
set(WHISPER_BUILD_EXAMPLES OFF CACHE BOOL "Disable upstream whisper.cpp examples" FORCE)
set(WHISPER_BUILD_SERVER OFF CACHE BOOL "Disable upstream whisper.cpp server" FORCE)

FetchContent_Declare(
    whispercpp
    GIT_REPOSITORY https://github.com/ggml-org/whisper.cpp.git
    GIT_TAG 5ed76e9a079962f1c85cfce44edd325c27ef1f97
    GIT_SHALLOW FALSE
    GIT_SUBMODULES_RECURSE TRUE)

FetchContent_MakeAvailable(whispercpp)

add_library(shruti_whisper SHARED
    src/shruti_whisper.cpp
    include/shruti_whisper.h)

target_compile_features(shruti_whisper PRIVATE cxx_std_17)
target_include_directories(shruti_whisper PRIVATE
    ${CMAKE_CURRENT_SOURCE_DIR}/include
    ${whispercpp_SOURCE_DIR}/include)
target_link_libraries(shruti_whisper PRIVATE whisper)
target_compile_definitions(shruti_whisper PRIVATE SHRUTI_WHISPER_BUILD)

set_target_properties(shruti_whisper PROPERTIES
    OUTPUT_NAME "shruti_whisper"
    WINDOWS_EXPORT_ALL_SYMBOLS OFF)

if(BUILD_TESTING)
    add_executable(shruti_whisper_smoke tests/smoke.cpp)
    target_include_directories(shruti_whisper_smoke PRIVATE include)
    target_link_libraries(shruti_whisper_smoke PRIVATE shruti_whisper)
    add_test(NAME shruti_whisper_smoke COMMAND shruti_whisper_smoke)
endif()
