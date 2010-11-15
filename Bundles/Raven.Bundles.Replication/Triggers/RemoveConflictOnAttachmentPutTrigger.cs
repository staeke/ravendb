﻿using Newtonsoft.Json.Linq;
using Raven.Database.Plugins;

namespace Raven.Bundles.Replication.Triggers
{
    public class RemoveConflictOnAttachmentPutTrigger : AbstractAttachmentPutTrigger
    {
        public override void OnPut(string key, byte[] data, JObject metadata)
        {
            if (ReplicationContext.IsInReplicationContext)
                return;

            using (ReplicationContext.Enter())
            {
                metadata.Remove(ReplicationConstants.RavenReplicationConflict);// you can't put conflicts

                var oldVersion = Database.GetStatic(key);
                if (oldVersion == null)
                    return;
                if (oldVersion.Metadata[ReplicationConstants.RavenReplicationConflict] == null)
                    return;
                // this is a conflict document, holding document keys in the 
                // values of the properties
                foreach (var prop in oldVersion.Metadata.Value<JArray>("Conflicts"))
                {
                    Database.DeleteStatic(prop.Value<string>(), null);
                }
            }
        }
    }
}