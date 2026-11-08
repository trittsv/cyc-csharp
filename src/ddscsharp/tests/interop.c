// Copyright(c) 2026 Cyclone DDS C# contributors
//
// This program and the accompanying materials are made available under the
// terms of the Eclipse Public License v. 2.0 which is available at
// http://www.eclipse.org/legal/epl-2.0, or the Eclipse Distribution License
// v. 1.0 which is available at
// http://www.eclipse.org/org/documents/edl-v10.php.
//
// SPDX-License-Identifier: EPL-2.0 OR BSD-3-Clause
#include "Types.h"
#include <dds/dds.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

int main(int argc, char **argv) {
  if (argc != 4) { fprintf(stderr, "usage: c_interop read|write DOMAIN TOPIC\n"); return 2; }
  dds_entity_t p = dds_create_participant((uint32_t)strtoul(argv[2], NULL, 10), NULL, NULL);
  dds_entity_t t = dds_create_topic(p, &Test_Message_desc, argv[3], NULL, NULL);
  dds_qos_t *q = dds_create_qos();
  dds_qset_reliability(q, DDS_RELIABILITY_RELIABLE, DDS_SECS(5));
  bool write = !strcmp(argv[1], "write");
  dds_entity_t e = write ? dds_create_writer(p, t, q, NULL) : dds_create_reader(p, t, q, NULL);
  dds_delete_qos(q);
  if (p < 0 || t < 0 || e < 0) { fprintf(stderr, "entity creation failed\n"); return 3; }
  if (write) {
    int count = 0;
    for (int i = 0; i < 500; i++) {
      count = dds_get_matched_subscriptions(e, NULL, 0);
      if (count > 0) break;
      dds_sleepfor(DDS_MSECS(10));
    }
    if (count <= 0) { dds_delete(p); return 4; }
    Test_Message message = { .id = 71, .text = "from native C", .value = 314 };
    int rc = dds_write(e, &message);
    if (rc == 0) rc = dds_wait_for_acks(e, DDS_SECS(5));
    dds_delete(p);
    return rc < 0 ? 5 : 0;
  }
  int result = 6;
  for (int i = 0; i < 500; i++) {
    void *samples[1] = {NULL};
    dds_sample_info_t info[1];
    int n = dds_take(e, samples, info, 1, 1);
    if (n < 0) { fprintf(stderr, "dds_take: %s (%d)\n", dds_strretcode(n), n); break; }
    if (n > 0) {
      const Test_Message *m = samples[0];
      bool match = info[0].valid_data && m->id == 72 && m->value == 271 && !strcmp(m->text, "from managed C#");
      if (!match) fprintf(stderr, "received valid=%d id=%d value=%d text=%s\n", info[0].valid_data, m->id, m->value, m->text ? m->text : "(null)");
      dds_return_loan(e, samples, n);
      if (match) { result = 0; puts("native reader received managed sample"); break; }
    }
    dds_sleepfor(DDS_MSECS(10));
  }
  if (result != 0) fprintf(stderr, "native reader timed out; matched writers=%d\n", dds_get_matched_publications(e, NULL, 0));
  dds_delete(p);
  return result;
}
