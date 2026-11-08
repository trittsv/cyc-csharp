// Copyright(c) 2026 Cyclone DDS C# contributors
//
// This program and the accompanying materials are made available under the
// terms of the Eclipse Public License v. 2.0 which is available at
// http://www.eclipse.org/legal/epl-2.0, or the Eclipse Distribution License
// v. 1.0 which is available at
// http://www.eclipse.org/org/documents/edl-v10.php.
//
// SPDX-License-Identifier: EPL-2.0 OR BSD-3-Clause
#if !defined(_WIN32)
#define _GNU_SOURCE
#include <dlfcn.h>
#else
/* windows.h must not introduce Winsock 1 before Cyclone's Winsock 2 headers. */
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <winsock2.h>
#include <windows.h>
#endif
#include <dds/dds.h>
#include "dds__entity.h"
#include <dds/ddsi/ddsi_typelib.h>
#include <dds/ddsi/ddsi_typebuilder.h>
#include <stdlib.h>
#include <string.h>
#if defined(_WIN32)
#define CS_EXPORT __declspec(dllexport)
#else
#define CS_EXPORT __attribute__((visibility("default")))
#endif

/* No managed declaration depends on the layout of dds_sample_info_t. */
typedef struct cs_info {
  uint32_t sample_state, view_state, instance_state, valid_data;
  int64_t timestamp;
  uint64_t instance_handle, publication_handle;
  uint32_t disposed_generation_count, no_writers_generation_count;
  uint32_t sample_rank, generation_rank, absolute_generation_rank;
} cs_info;

typedef struct cs_batch {
  dds_entity_t reader;
  int32_t count;
  void **samples;
  dds_sample_info_t *infos;
} cs_batch;

CS_EXPORT uint32_t cs_abi_version(void) { return 1; }
/* P/Invoke must use the exact module linked to this bridge. Loading a second
   copy creates two independent DDS runtimes with incompatible entity handles. */
CS_EXPORT const char *cs_dds_library_path(void) {
#if defined(_WIN32)
  static char path[32768];
  HMODULE module;
  if (!GetModuleHandleExA(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
      (LPCSTR)(void *)&dds_create_participant, &module)) return NULL;
  if (!GetModuleFileNameA(module, path, sizeof(path))) return NULL;
  return path;
#else
  Dl_info info;
  if (!dladdr((void *)&dds_create_participant, &info)) return NULL;
  return info.dli_fname;
#endif
}
CS_EXPORT void cs_free_sample_contents(void *sample, const dds_topic_descriptor_t *descriptor) {
  dds_sample_free(sample, descriptor, DDS_FREE_CONTENTS);
}

CS_EXPORT int32_t cs_read(dds_entity_t reader, uint32_t maximum, uint32_t mask,
                          uint32_t take, uint64_t instance, cs_batch **result) {
  *result = NULL;
  if (maximum == 0 || maximum > INT32_MAX) return DDS_RETCODE_BAD_PARAMETER;
  cs_batch *b = calloc(1, sizeof(*b));
  if (!b) return DDS_RETCODE_OUT_OF_RESOURCES;
  b->samples = calloc(maximum, sizeof(*b->samples));
  b->infos = calloc(maximum, sizeof(*b->infos));
  if (!b->samples || !b->infos) {
    free(b->samples); free(b->infos); free(b);
    return DDS_RETCODE_OUT_OF_RESOURCES;
  }
  b->reader = reader;
  if (instance) {
    b->count = take
      ? dds_take_instance_mask(reader, b->samples, b->infos, maximum, maximum, instance, mask)
      : dds_read_instance_mask(reader, b->samples, b->infos, maximum, maximum, instance, mask);
  } else {
    b->count = take
      ? dds_take_mask(reader, b->samples, b->infos, maximum, maximum, mask)
      : dds_read_mask(reader, b->samples, b->infos, maximum, maximum, mask);
  }
  if (b->count < 0) {
    int32_t error = b->count;
    free(b->samples); free(b->infos); free(b);
    return error;
  }
  *result = b;
  return b->count;
}

CS_EXPORT const void *cs_batch_get(const cs_batch *b, uint32_t index, cs_info *out) {
  if (!b || index >= (uint32_t)b->count) return NULL;
  const dds_sample_info_t *i = &b->infos[index];
  memset(out, 0, sizeof(*out));
  out->sample_state = i->sample_state;
  out->view_state = i->view_state;
  out->instance_state = i->instance_state;
  out->valid_data = i->valid_data;
  out->timestamp = i->source_timestamp;
  out->instance_handle = i->instance_handle;
  out->publication_handle = i->publication_handle;
  out->disposed_generation_count = i->disposed_generation_count;
  out->no_writers_generation_count = i->no_writers_generation_count;
  out->sample_rank = i->sample_rank;
  out->generation_rank = i->generation_rank;
  out->absolute_generation_rank = i->absolute_generation_rank;
  return b->samples[index];
}

CS_EXPORT int32_t cs_batch_free(cs_batch *b) {
  if (!b) return DDS_RETCODE_OK;
  int32_t rc = b->count ? dds_return_loan(b->reader, b->samples, b->count) : DDS_RETCODE_OK;
  free(b->samples); free(b->infos); free(b);
  return rc;
}

CS_EXPORT size_t cs_sequence_size(void) { return sizeof(dds_sequence_t); }
CS_EXPORT void cs_sequence_set(dds_sequence_t *seq, uint32_t n, void *buffer) {
  seq->_maximum = n; seq->_length = n; seq->_buffer = buffer; seq->_release = false;
}
CS_EXPORT uint32_t cs_sequence_length(const dds_sequence_t *seq) { return seq->_length; }
CS_EXPORT void *cs_sequence_buffer(const dds_sequence_t *seq) { return seq->_buffer; }

/* Isolate the pinned Cyclone DDS typelib ABI in the common runtime bridge.
   Serialized XTypes metadata comes from idlc, including keys, annotations and
   recursive dependencies. No generated native code is loaded by applications. */
CS_EXPORT int32_t cs_descriptor_create(dds_entity_t participant,
    const unsigned char *information, uint32_t information_size,
    const unsigned char *mapping, uint32_t mapping_size,
    uint32_t expected_size, uint32_t expected_alignment,
    dds_topic_descriptor_t **result) {
  *result = NULL;
  ddsi_typeinfo_t *info = ddsi_typeinfo_deser(information, information_size);
  ddsi_typemap_t *map = ddsi_typemap_deser(mapping, mapping_size);
  if (!info || !map) {
    if (info) ddsi_typeinfo_free(info);
    if (map) { ddsi_typemap_fini(map); dds_free(map); }
    return DDS_RETCODE_BAD_PARAMETER;
  }
  dds_entity *entity;
  dds_return_t ret = dds_entity_pin(participant, &entity);
  if (ret == DDS_RETCODE_OK) {
    struct ddsi_domaingv *gv = &entity->m_domain->gv;
    struct ddsi_type *minimal = NULL, *complete = NULL;
    ret = ddsi_type_add(gv, &minimal, &complete, info, map);
    if (ret == DDS_RETCODE_OK) {
      dds_topic_descriptor_t *descriptor = dds_alloc(sizeof(*descriptor));
      if (!descriptor) ret = DDS_RETCODE_OUT_OF_RESOURCES;
      else {
        ret = ddsi_topic_descriptor_from_type(gv, descriptor, complete);
        if (ret == DDS_RETCODE_OK &&
            (descriptor->m_size != expected_size || descriptor->m_align != expected_alignment)) {
          ddsi_topic_descriptor_fini(descriptor);
          ret = DDS_RETCODE_BAD_PARAMETER;
        }
        if (ret == DDS_RETCODE_OK) *result = descriptor;
        else dds_free(descriptor);
      }
    }
    if (minimal) ddsi_type_unref(gv, minimal);
    if (complete) ddsi_type_unref(gv, complete);
    dds_entity_unpin(entity);
  }
  ddsi_typeinfo_free(info);
  ddsi_typemap_fini(map); dds_free(map);
  return ret;
}
CS_EXPORT void cs_descriptor_free(dds_topic_descriptor_t *descriptor) {
  if (descriptor) { ddsi_topic_descriptor_fini(descriptor); dds_free(descriptor); }
}
