﻿using Newtonsoft.Json.Linq;
using Raven.Database.Plugins;

namespace Raven.Bundles.Replication.Triggers
{
    public class AttachmentAncestryPutTrigger : AbstractAttachmentPutTrigger
    {
        private ReplicationHiLo hiLo;
        public override void Initialize()
        {
            base.Initialize();
            hiLo = new ReplicationHiLo
            {
                Database = Database
            };
        }

        public override void OnPut(string key, byte[] data, JObject metadata)
        {
            if (key.StartsWith("Raven/")) // we don't deal with system attachment
                return;
            if (ReplicationContext.IsInReplicationContext)
                return;
            var doc = Database.Get(key, null);
            if (doc != null)
            {
                metadata[ReplicationConstants.RavenReplicationParentVersion] =
                    doc.Metadata[ReplicationConstants.RavenReplicationVersion];
                metadata[ReplicationConstants.RavenReplicationParentSource] =
                    doc.Metadata[ReplicationConstants.RavenReplicationSource];
            }
            metadata[ReplicationConstants.RavenReplicationVersion] = JToken.FromObject(hiLo.NextId());
            metadata[ReplicationConstants.RavenReplicationSource] = JToken.FromObject(Database.TransactionalStorage.Id);
        }
    }
}