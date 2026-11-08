# Copyright(c) 2026 Cyclone DDS C# contributors
#
# This program and the accompanying materials are made available under the
# terms of the Eclipse Public License v. 2.0 which is available at
# http://www.eclipse.org/legal/epl-2.0, or the Eclipse Distribution License
# v. 1.0 which is available at
# http://www.eclipse.org/org/documents/edl-v10.php.
#
# SPDX-License-Identifier: EPL-2.0 OR BSD-3-Clause
function(cyclonedds_generate_csharp)
  cmake_parse_arguments(CS "" "TARGET;FILE" "INCLUDES;DEPENDS" ${ARGN})
  if(NOT CS_TARGET OR NOT CS_FILE)
    message(FATAL_ERROR "cyclonedds_generate_csharp requires TARGET and FILE")
  endif()
  get_filename_component(stem "${CS_FILE}" NAME_WE)
  set(out "${CMAKE_CURRENT_BINARY_DIR}/generated/${CS_TARGET}")
  set(include_flags)
  foreach(dir IN LISTS CS_INCLUDES)
    list(APPEND include_flags "-I${dir}")
  endforeach()
  add_custom_command(OUTPUT "${out}/${stem}.cs"
    COMMAND ${CMAKE_COMMAND} -E make_directory "${out}"
    COMMAND CycloneDDS::idlc ${include_flags} -x final -l $<TARGET_FILE:cycloneddsidlcsharp> -o "${out}" "${CS_FILE}"
    DEPENDS "${CS_FILE}" cycloneddsidlcsharp ${CS_DEPENDS}
    VERBATIM)
  add_custom_target(${CS_TARGET} DEPENDS "${out}/${stem}.cs")
  set(${CS_TARGET}_OUTPUT_DIR "${out}" PARENT_SCOPE)
endfunction()
