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
