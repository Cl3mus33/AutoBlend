#
# Includes and Sources
#
include_directories("include")
file(GLOB_RECURSE HEADERS CONFIGURE_DEPENDS include/*.hpp)
file(GLOB_RECURSE SOURCES CONFIGURE_DEPENDS src/*.cpp)

#
# Executable
#
set(EXE_NAME "AutoBlend")
add_executable(${EXE_NAME}
    ${SOURCES}
    ${HEADERS}
    ${CMAKE_CURRENT_SOURCE_DIR}/resources/icon.rc
)
target_compile_options(${EXE_NAME} PRIVATE /utf-8)
target_include_directories(${EXE_NAME} PUBLIC include)

#
# VCPKG Dependencies
#
find_package(spdlog REQUIRED CONFIG)
find_package(nlohmann_json REQUIRED CONFIG)
find_package(cpptrace REQUIRED CONFIG)
find_package(wxWidgets REQUIRED COMPONENTS core base)

target_link_libraries(${EXE_NAME} PRIVATE
    spdlog::spdlog
    nlohmann_json::nlohmann_json
    cpptrace::cpptrace
    ${wxWidgets_LIBRARIES}
    Shlwapi
)

#
# AutoBlend.NativeExport (C#/DNNE) build
#
# The whole Mutagen/niflysharp patch pipeline lives in AutoBlend.Core (C#) - this executable never
# touches a NIF or an ESP directly. DNNE turns AutoBlend.NativeExport's [UnmanagedCallersOnly]
# exports (Exports.cs: load_settings/save_settings/detect_mo2_profile/start_patch_run/get_progress/
# free_string) into a native shim (AutoBlend.NativeExportNE.lib/.dll/.h) this executable links
# against and delay-loads, exactly the pattern AutoSeasons' own ASMutagenWrapper/ASMutagenNE uses
# for its Mutagen-based logic (confirmed by reading AutoSeasons' ASMutagen/CMakeLists.txt) - minus
# the FlatBuffers/wrapper-DLL indirection, which AutoBlend doesn't need since there's no C++-side
# business logic calling into it.
#
set(NATIVEEXPORT_PROJECT_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../../src/AutoBlend.NativeExport")
set(NATIVEEXPORT_CSPROJ "${NATIVEEXPORT_PROJECT_DIR}/AutoBlend.NativeExport.csproj")
set(NATIVEEXPORT_BUILD_DIR "${CMAKE_BINARY_DIR}/AutoBlend.NativeExport")
set(NATIVEEXPORT_BIN_DIR "${NATIVEEXPORT_BUILD_DIR}/bin")
set(NATIVEEXPORT_OBJ_DIR "${NATIVEEXPORT_BUILD_DIR}/obj")
set(NATIVEEXPORT_NUGET_DIR "${NATIVEEXPORT_BUILD_DIR}/.nuget/packages")

set(NATIVEEXPORT_LIB "${NATIVEEXPORT_BIN_DIR}/AutoBlend.NativeExportNE.lib")

target_link_libraries(${EXE_NAME} PRIVATE "${NATIVEEXPORT_LIB}")
target_include_directories(${EXE_NAME} PRIVATE "${NATIVEEXPORT_BIN_DIR}")

if(MSVC)
    target_link_options(${EXE_NAME} PRIVATE "/DELAYLOAD:AutoBlend.NativeExportNE.dll")
    target_link_libraries(${EXE_NAME} PRIVATE delayimp)
endif()

# NuGet restore during configure step
execute_process(
    COMMAND dotnet restore "${NATIVEEXPORT_CSPROJ}"
        --packages "${NATIVEEXPORT_NUGET_DIR}"
        -r win-x64
        /p:BaseIntermediateOutputPath=${NATIVEEXPORT_OBJ_DIR}/
        /p:BaseOutputPath=${NATIVEEXPORT_BIN_DIR}/
    WORKING_DIRECTORY "${NATIVEEXPORT_PROJECT_DIR}"
    RESULT_VARIABLE _nuget_restore_result
)

if(NOT _nuget_restore_result EQUAL 0)
    message(FATAL_ERROR "NuGet restore failed for AutoBlend.NativeExport.csproj")
endif()

if(${CMAKE_BUILD_TYPE} STREQUAL "Release" OR ${CMAKE_BUILD_TYPE} STREQUAL "RelWithDebInfo")
    set(MSBUILD_CONFIGURATION "Release")
elseif(${CMAKE_BUILD_TYPE} STREQUAL "Debug")
    set(MSBUILD_CONFIGURATION "Debug")
else()
    set(MSBUILD_CONFIGURATION "Release")
endif()

file(GLOB_RECURSE CSHARP_SOURCES CONFIGURE_DEPENDS
    "${NATIVEEXPORT_PROJECT_DIR}/*.cs"
    "${NATIVEEXPORT_PROJECT_DIR}/../AutoBlend.Core/*.cs"
)
list(APPEND CSHARP_SOURCES "${NATIVEEXPORT_CSPROJ}")

add_custom_command(
    OUTPUT ${NATIVEEXPORT_LIB}
    # OutputPath controls where DNNE emits AutoBlend.NativeExportNE.lib (used by the C++ linker at
    # build time). --output (PublishDir) controls where the runtime and managed DLLs land for
    # shipping. Both point to the same directory so all artifacts end up in one place.
    COMMAND dotnet publish "${NATIVEEXPORT_CSPROJ}"
        -c ${MSBUILD_CONFIGURATION}
        -r win-x64
        --self-contained true
        --no-restore
        -p:Platform=x64
        -p:OutputPath=${NATIVEEXPORT_BIN_DIR}/
        -p:BaseIntermediateOutputPath=${NATIVEEXPORT_OBJ_DIR}/
        --output ${NATIVEEXPORT_BIN_DIR}/
    DEPENDS
        ${CSHARP_SOURCES}
    WORKING_DIRECTORY ${NATIVEEXPORT_PROJECT_DIR}
    COMMENT "Building AutoBlend.NativeExportNE.lib (self-contained)"
)

add_custom_target(AutoBlendNativeExportNE
    DEPENDS ${NATIVEEXPORT_LIB}
)

add_dependencies(${EXE_NAME} AutoBlendNativeExportNE)

#
# C# DLLs Copy
#
# Deliberately named AutoBlend_dotnetlib rather than the generic "dotnetlib" - AutoSeasons ships
# its own AutoSeasons_dotnetlib/ folder full of similar self-contained .NET runtime files, so a
# generic name would collide almost entirely when both tools are installed as MO2 mods in the same
# modlist. A unique folder name means no overlapping paths, so no MO2 conflict.
#
add_custom_target(CopyAutoBlendNativeExportDLLs ALL
    COMMAND ${CMAKE_COMMAND} -E make_directory "${CMAKE_BINARY_DIR}/bin/AutoBlend_dotnetlib"
    COMMAND ${CMAKE_COMMAND} -E copy_directory "${NATIVEEXPORT_BIN_DIR}/" "${CMAKE_BINARY_DIR}/bin/AutoBlend_dotnetlib"
    COMMENT "Copying AutoBlend.NativeExport DLLs to build/bin/AutoBlend_dotnetlib"
)

add_dependencies(CopyAutoBlendNativeExportDLLs AutoBlendNativeExportNE)
add_dependencies(${EXE_NAME} CopyAutoBlendNativeExportDLLs)

#
# autoblend.ini
#
# Same convention as AutoSeasons' own AS.ini: an otherwise-unused marker file (content "Empty")
# sitting next to the exe, so a mod manager sees a recognizable per-mod ini file to attach to this
# folder instead of nothing at all.
#
add_custom_command(
    TARGET ${EXE_NAME} POST_BUILD
    COMMAND ${CMAKE_COMMAND} -E copy
        "${CMAKE_CURRENT_SOURCE_DIR}/resources/autoblend.ini"
        "${CMAKE_BINARY_DIR}/bin/autoblend.ini"
    COMMENT "Copying autoblend.ini to build/bin"
)

#
# GUI translations
#
# Same convention as AutoSeasons' own AutoSeasons_translations/ - one JSON file per language, read
# by ABLocale at startup from a folder named after this tool specifically (same collision-avoidance
# reasoning as AutoBlend_dotnetlib above).
#
add_custom_command(
    TARGET ${EXE_NAME} POST_BUILD
    COMMAND ${CMAKE_COMMAND} -E make_directory "${CMAKE_BINARY_DIR}/bin/AutoBlend_translations"
    COMMAND ${CMAKE_COMMAND} -E copy_directory
        "${CMAKE_CURRENT_SOURCE_DIR}/resources/AutoBlend_translations"
        "${CMAKE_BINARY_DIR}/bin/AutoBlend_translations"
    COMMENT "Copying AutoBlend_translations to build/bin"
)

#
# texconv (DirectXTex CLI tool)
#
# AutoBlend.Core (Scanning/MissingTextureGenerator.cs) shells out to this to synthesize a missing
# statics/blending texture from whatever diffuse is actually winning in the load order (strips its
# alpha channel to opaque via `--swizzle rgb1`, verified to reproduce Vanaheimr's own hand-authored
# statics textures near pixel-for-pixel). The vcpkg "directxtex[tools]" feature just downloads
# Microsoft's own prebuilt release binary (no local compile), so this copy is cheap. Landed in
# AutoBlend_dotnetlib alongside AutoBlend.Core.dll rather than bin/ directly, since that's the one
# folder AutoBlend.Core can locate via AppContext.BaseDirectory regardless of which shell (this one,
# or the WPF app) is hosting it.
#
add_custom_command(
    TARGET ${EXE_NAME} POST_BUILD
    COMMAND ${CMAKE_COMMAND} -E copy
        "${CMAKE_BINARY_DIR}/vcpkg_installed/${VCPKG_TARGET_TRIPLET}/tools/directxtex/texconv.exe"
        "${CMAKE_BINARY_DIR}/bin/AutoBlend_dotnetlib/texconv.exe"
    COMMENT "Copying texconv.exe to build/bin/AutoBlend_dotnetlib"
)

#
# texconv.exe's own VC++ runtime DLLs
#
# Unlike AutoBlend.exe itself (statically linked, see CMAKE_MSVC_RUNTIME_LIBRARY in the root
# CMakeLists.txt), texconv.exe is a prebuilt Microsoft binary we don't compile ourselves, so our
# static-linkage settings don't apply to it - dumpbin /dependents confirms it dynamically needs
# MSVCP140.dll, VCRUNTIME140.dll, VCRUNTIME140_1.dll (all three from the CRT redist) and
# VCOMP140.DLL (from the OpenMP redist). Without these next to it, texconv.exe fails to start on
# any machine that doesn't already have the matching VC++ Redistributable installed. Copied from
# the toolset's own Redist folder (populated by vcvars64.bat, which build.bat always calls first)
# rather than assuming the target machine already has them - same fix AutoSeasons applied to its
# own exe for vcomp140.dll (see AutoSeasons/CMakeLists.txt).
#
if(NOT DEFINED ENV{VCToolsRedistDir})
    message(FATAL_ERROR "VCToolsRedistDir is not set - configure/build from a Developer Command Prompt (or via vcvars64.bat, like build.bat does) so texconv.exe's runtime DLLs can be located.")
endif()

file(GLOB TEXCONV_CRT_DLLS
    "$ENV{VCToolsRedistDir}x64/Microsoft.VC*.CRT/msvcp140.dll"
    "$ENV{VCToolsRedistDir}x64/Microsoft.VC*.CRT/vcruntime140.dll"
    "$ENV{VCToolsRedistDir}x64/Microsoft.VC*.CRT/vcruntime140_1.dll"
)
file(GLOB TEXCONV_OPENMP_DLL "$ENV{VCToolsRedistDir}x64/Microsoft.VC*.OpenMP/vcomp140.dll")
list(GET TEXCONV_OPENMP_DLL 0 TEXCONV_OPENMP_DLL)

add_custom_command(
    TARGET ${EXE_NAME} POST_BUILD
    COMMAND ${CMAKE_COMMAND} -E copy_if_different
        ${TEXCONV_CRT_DLLS}
        "${TEXCONV_OPENMP_DLL}"
        "${CMAKE_BINARY_DIR}/bin/AutoBlend_dotnetlib/"
    COMMENT "Copying texconv.exe's VC++ runtime DLLs to build/bin/AutoBlend_dotnetlib"
)
