﻿using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Raven.Database;
using Raven.Database.Impl;
using Raven.Database.Storage;
using Raven.Storage.Managed.Impl;
using System.Linq;

namespace Raven.Storage.Managed
{
    public class QueueStorageActions : IQueueStorageActions
    {
        private readonly TableStorage storage;
        private readonly IUuidGenerator generator;

        public QueueStorageActions(TableStorage storage, IUuidGenerator generator)
        {
            this.storage = storage;
            this.generator = generator;
        }

        public void EnqueueToQueue(string name, byte[] data)
        {
            storage.Queues.Put(new JObject
            {
                {"name", name},
                {"id", generator.CreateSequentialUuid().ToByteArray()},
                {"reads", 0}
            }, data);
        }

        public IEnumerable<Tuple<byte[], object>> PeekFromQueue(string name)
        {
            foreach (var queuedMsgKey in storage.Queues["ByName"].SkipTo(new JObject
            {
                {"name", name}
            }).TakeWhile(x=> StringComparer.InvariantCultureIgnoreCase.Equals(x.Value<string>("name"), name)))
            {
                var readResult = storage.Queues.Read(queuedMsgKey);
                if(readResult == null)
                    yield break;

                if (readResult.Key.Value<int>("reads") > 5) //      // read too much, probably poison message, remove it
                {
                    storage.Queues.Remove(readResult.Key);
                    continue;
                }

                readResult.Key["reads"] = readResult.Key.Value<int>("reads") + 1;
                storage.Queues.UpdateKey(readResult.Key);

                yield return new Tuple<byte[], object>(
                    readResult.Data(),
                    readResult.Key.Value<byte[]>("id")
                    );
            }
        }

        public void DeleteFromQueue(string name, object id)
        {
            storage.Queues.Remove(new JObject
                {
                    {"name", name},
                    {"id", (byte[])id}
                });
        }
    }
}