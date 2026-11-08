# Copyright(c) 2020 to 2022 ZettaScale Technology and others
# Copyright(c) 2026 Cyclone DDS C# contributors
#
# This program and the accompanying materials are made available under the
# terms of the Eclipse Public License v. 2.0 which is available at
# http://www.eclipse.org/legal/epl-2.0, or the Eclipse Distribution License
# v. 1.0 which is available at
# http://www.eclipse.org/org/documents/edl-v10.php.
#
# SPDX-License-Identifier: EPL-2.0 OR BSD-3-Clause
function(cyclonedds_csharp_example name types)
  cyclonedds_generate_csharp(TARGET ${types} FILE "${CMAKE_CURRENT_SOURCE_DIR}/${name}.idl")
  string(TOLOWER "${name}" target_suffix)
  add_custom_target(managed_${target_suffix} ALL
    COMMAND "${DOTNET_EXECUTABLE}" build "${CMAKE_CURRENT_SOURCE_DIR}/${name}.csproj"
      --configuration Release --nologo -p:Platform=AnyCPU "-p:DdsGeneratedDirectory=${${types}_OUTPUT_DIR}"
    DEPENDS ${types} cycloneddscsharp VERBATIM)
  # These projects share a managed runtime dependency. Serialize managed builds
  # so concurrent MSBuild processes do not write its output at the same time.
  get_property(previous GLOBAL PROPERTY CSHARP_PREVIOUS_EXAMPLE)
  if(previous)
    add_dependencies(managed_${target_suffix} ${previous})
  elseif(TARGET managed_tests)
    add_dependencies(managed_${target_suffix} managed_tests)
  endif()
  set_property(GLOBAL PROPERTY CSHARP_PREVIOUS_EXAMPLE managed_${target_suffix})
endfunction()

add_subdirectory(HelloWorld)
add_subdirectory(Throughput)
add_subdirectory(RoundTrip)

if(BUILD_TESTING)
  add_test(NAME csharp_examples COMMAND "${Python3_EXECUTABLE}" "${PROJECT_SOURCE_DIR}/scripts/test-examples.py"
    --build "${PROJECT_BINARY_DIR}" --native "$<TARGET_FILE_DIR:cycloneddscsharp>")
  set_tests_properties(csharp_examples PROPERTIES TIMEOUT 180)
endif()
