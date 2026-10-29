cmake_minimum_required(VERSION 3.28)
project(PenguLODCompute LANGUAGES C)
find_package(Vulkan REQUIRED)
find_package(Python3 COMPONENTS Interpreter REQUIRED)
find_program(GLSLANG_VALIDATOR NAMES glslangValidator glslang REQUIRED
    HINTS "${VCPKG_INSTALLED_DIR}/${VCPKG_TARGET_TRIPLET}/tools/glslang")
add_custom_command(OUTPUT "${CMAKE_CURRENT_BINARY_DIR}/bc7_spv.h"
    COMMAND "${GLSLANG_VALIDATOR}" -V --target-env vulkan1.0 "${CMAKE_CURRENT_SOURCE_DIR}/bc7.comp" -o "${CMAKE_CURRENT_BINARY_DIR}/bc7.spv"
    COMMAND "${Python3_EXECUTABLE}" "${CMAKE_CURRENT_SOURCE_DIR}/../../tools/spirv-header.py" "${CMAKE_CURRENT_BINARY_DIR}/bc7.spv" "${CMAKE_CURRENT_BINARY_DIR}/bc7_spv.h"
    DEPENDS bc7.comp ../../tools/spirv-header.py VERBATIM)
add_library(pengulod_compute SHARED backend.c "${CMAKE_CURRENT_BINARY_DIR}/bc7_spv.h")
target_compile_features(pengulod_compute PRIVATE c_std_11)
target_include_directories(pengulod_compute PRIVATE "${CMAKE_CURRENT_BINARY_DIR}")
target_link_libraries(pengulod_compute PRIVATE Vulkan::Vulkan)
if(MSVC)
    target_compile_options(pengulod_compute PRIVATE /W4 /utf-8)
else()
    target_compile_options(pengulod_compute PRIVATE -Wall -Wextra -Werror)
endif()
