cmake_minimum_required (VERSION 3.31)

#
# Project
#
set(PROJECT_NAME "AutoBlend")

# Our vcpkg triplet builds all dependencies with a static CRT (VCPKG_CRT_LINKAGE static), so our
# own targets must match or every link step mismatches (LNK2038) against vcpkg's static libs.
# Must be set before project() - it controls how compiler/linker defaults get detected.
cmake_policy(SET CMP0091 NEW)
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")

#
# Using C++20 Standard for all modules
#
set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

#
# MSVC / Windows SDK
#
add_compile_options("/Zc:__cplusplus")
if (POLICY CMP0141)
  cmake_policy(SET CMP0141 NEW)
  set(CMAKE_MSVC_DEBUG_INFORMATION_FORMAT "$<IF:$<AND:$<C_COMPILER_ID:MSVC>,$<CXX_COMPILER_ID:MSVC>>,$<$<CONFIG:Debug,RelWithDebInfo>:EditAndContinue>,$<$<CONFIG:Debug,RelWithDebInfo>:ProgramDatabase>>")
endif()
cmake_policy(SET CMP0167 NEW)

# Disables conflicting min/max macros in Windows.h
add_compile_definitions(NOMINMAX)
add_compile_definitions(WIN32_LEAN_AND_MEAN)
add_compile_definitions(NOSOCKETTYPE)
add_compile_definitions(_DISABLE_CONSTEXPR_MUTEX_CONSTRUCTOR)
add_compile_definitions(_WIN32_WINNT=0x0A00)
add_compile_definitions(UNICODE _UNICODE)

project(${PROJECT_NAME} VERSION 0.1.0)

set(CMAKE_RUNTIME_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin)

#
# Submodules
#
add_subdirectory(AutoBlend)
add_subdirectory(AutoBlendTexTools)

#
# No VC Redist DLL copy step: CMAKE_MSVC_RUNTIME_LIBRARY above statically links the CRT into
# every target, so msvcp140/vcruntime140/concrt140/vccorlib140 aren't runtime dependencies at all -
# copying them would just be dead weight that overlaps with other tools' (dynamically-linked)
# copies when several are installed as MO2 mods in the same instance.
#
