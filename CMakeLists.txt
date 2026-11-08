# Copyright(c) 2026 Cyclone DDS C# contributors
#
# This program and the accompanying materials are made available under the
# terms of the Eclipse Public License v. 2.0 which is available at
# http://www.eclipse.org/legal/epl-2.0, or the Eclipse Distribution License
# v. 1.0 which is available at
# http://www.eclipse.org/org/documents/edl-v10.php.
#
# SPDX-License-Identifier: EPL-2.0 OR BSD-3-Clause
cmake_minimum_required(VERSION 3.20)
file(STRINGS "${CMAKE_CURRENT_SOURCE_DIR}/VERSION" CYCLONEDDS_CSHARP_VERSION LIMIT_COUNT 1)
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
project(CycloneDDSCSharp VERSION "${CYCLONEDDS_CSHARP_VERSION}" LANGUAGES C)
find_package(CycloneDDS 11.0.1 REQUIRED)
include(CTest)
set(CMAKE_C_STANDARD 11)
set(CMAKE_C_STANDARD_REQUIRED ON)
set(CMAKE_RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/lib")
set(CMAKE_LIBRARY_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/lib")
if(APPLE)
  set(CMAKE_BUILD_RPATH "@loader_path")
  set(CMAKE_INSTALL_RPATH "@loader_path")
elseif(UNIX)
  set(CMAKE_BUILD_RPATH "$ORIGIN")
  set(CMAKE_INSTALL_RPATH "$ORIGIN")
endif()
# The bridge uses the pinned typelib implementation to consume idlc's XTypes blobs.
set(CYCLONEDDS_SOURCE_DIR "${CMAKE_CURRENT_SOURCE_DIR}/build/_deps/cyclonedds-src" CACHE PATH "Source tree of the matching pinned Cyclone DDS runtime")
if(NOT EXISTS "${CYCLONEDDS_SOURCE_DIR}/src/core/ddsc/src/dds__entity.h")
  message(FATAL_ERROR "Set CYCLONEDDS_SOURCE_DIR to the matching Cyclone DDS source tree (scripts/bootstrap-native.sh provides it).")
endif()
add_library(cycloneddscsharp SHARED src/ddscsharp/Native/bridge.c)
target_include_directories(cycloneddscsharp PRIVATE "${CYCLONEDDS_SOURCE_DIR}/src/core/ddsc/src")
target_link_libraries(cycloneddscsharp PRIVATE CycloneDDS::ddsc ${CMAKE_DL_LIBS})
target_compile_definitions(cycloneddscsharp PRIVATE CS_BUILD)
if(WIN32)
  set(ddsc_filename "ddsc.dll")
else()
  set(ddsc_filename "$<TARGET_SONAME_FILE_NAME:CycloneDDS::ddsc>")
endif()
add_custom_command(TARGET cycloneddscsharp POST_BUILD
  COMMAND ${CMAKE_COMMAND} -E copy_if_different $<TARGET_FILE:CycloneDDS::ddsc>
    "$<TARGET_FILE_DIR:cycloneddscsharp>/${ddsc_filename}")
option(BUILD_IDLCSHARP "Build the host C# IDL compiler backend" ON)
set_target_properties(cycloneddscsharp PROPERTIES VERSION ${PROJECT_VERSION} SOVERSION ${PROJECT_VERSION_MAJOR})
if(MSVC)
  target_compile_options(cycloneddscsharp PRIVATE /W4)
else()
  target_compile_options(cycloneddscsharp PRIVATE -Wall -Wextra -Werror)
endif()
if(BUILD_IDLCSHARP)
  add_library(cycloneddsidlcsharp SHARED src/idlcsharp/generator.c)
  target_link_libraries(cycloneddsidlcsharp PRIVATE CycloneDDS::idl)
  set_target_properties(cycloneddsidlcsharp PROPERTIES VERSION ${PROJECT_VERSION} SOVERSION ${PROJECT_VERSION_MAJOR})
  if(MSVC)
    target_compile_options(cycloneddsidlcsharp PRIVATE /W4)
  else()
    target_compile_options(cycloneddsidlcsharp PRIVATE -Wall -Wextra -Werror)
  endif()
endif()
include(cmake/GenerateCSharp.cmake)
option(BUILD_CSHARP_EXAMPLES "Build HelloWorld, Throughput and RoundTrip examples" ON)
if(BUILD_TESTING OR BUILD_CSHARP_EXAMPLES)
  if(NOT BUILD_IDLCSHARP)
    message(FATAL_ERROR "Managed tests/examples require BUILD_IDLCSHARP=ON; disable both for runtime-only cross builds.")
  endif()
  find_program(DOTNET_EXECUTABLE dotnet REQUIRED)
endif()
if(BUILD_TESTING)
cyclonedds_generate_csharp(TARGET test_types FILE "${CMAKE_CURRENT_SOURCE_DIR}/src/ddscsharp/tests/idl/Types.idl")
# Native C interoperability is an independent peer, not part of C# type generation.
add_custom_command(OUTPUT "${test_types_OUTPUT_DIR}/Types.c" "${test_types_OUTPUT_DIR}/Types.h"
  COMMAND CycloneDDS::idlc -x final -o "${test_types_OUTPUT_DIR}" "${CMAKE_CURRENT_SOURCE_DIR}/src/ddscsharp/tests/idl/Types.idl"
  DEPENDS "${CMAKE_CURRENT_SOURCE_DIR}/src/ddscsharp/tests/idl/Types.idl" VERBATIM)
add_executable(c_interop src/ddscsharp/tests/interop.c "${test_types_OUTPUT_DIR}/Types.c")
target_include_directories(c_interop PRIVATE "${test_types_OUTPUT_DIR}")
target_link_libraries(c_interop PRIVATE CycloneDDS::ddsc)
add_custom_target(managed_tests ALL
  COMMAND "${DOTNET_EXECUTABLE}" build "${CMAKE_CURRENT_SOURCE_DIR}/src/ddscsharp/tests/Org.Eclipse.CycloneDDS.Tests.csproj"
    --configuration Release --nologo "-p:DdsGeneratedDirectory=${test_types_OUTPUT_DIR}"
  DEPENDS test_types cycloneddscsharp c_interop VERBATIM)
add_test(NAME ddscsharp COMMAND ${CMAKE_COMMAND} -E env
  "CYCLONEDDS_CSHARP_NATIVE=$<TARGET_FILE_DIR:cycloneddscsharp>"
  "${DOTNET_EXECUTABLE}" "${CMAKE_CURRENT_SOURCE_DIR}/src/ddscsharp/tests/bin/Release/net9.0/Org.Eclipse.CycloneDDS.Tests.dll")
find_package(Python3 COMPONENTS Interpreter REQUIRED)
add_test(NAME idlcsharp COMMAND "${Python3_EXECUTABLE}" "${CMAKE_CURRENT_SOURCE_DIR}/scripts/test-generator.py"
  --build "${CMAKE_BINARY_DIR}" --idlc "$<TARGET_FILE:CycloneDDS::idlc>"
  --plugin "$<TARGET_FILE:cycloneddsidlcsharp>" --prefix "${CycloneDDS_DIR}/../../..")
endif()
if(BUILD_CSHARP_EXAMPLES)
  add_subdirectory(examples)
endif()
if(BUILD_IDLCSHARP)
  install(TARGETS cycloneddsidlcsharp RUNTIME DESTINATION bin LIBRARY DESTINATION lib ARCHIVE DESTINATION lib)
endif()
install(TARGETS cycloneddscsharp RUNTIME DESTINATION bin LIBRARY DESTINATION lib ARCHIVE DESTINATION lib)
install(FILES cmake/GenerateCSharp.cmake DESTINATION lib/cmake/CycloneDDSCSharp)
