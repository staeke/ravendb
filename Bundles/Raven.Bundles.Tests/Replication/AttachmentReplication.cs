using System.Threading;
using Newtonsoft.Json.Linq;
using Raven.Client.Exceptions;
using Raven.Database.Data;
using Xunit;

namespace Raven.Bundles.Tests.Replication
{
    public class AttachmentReplication : ReplicationBase
    {
        [Fact]
        public void Can_replicate_between_two_instances()
        {
            var store1 = CreateStore();
            var store2 = CreateStore();

            TellFirstInstanceToReplicateToSecondInstance();

            store1.DatabaseCommands.PutAttachment("ayende", null, new byte[] {1, 2, 3}, new JObject());


            Attachment attachment = null;
            for (int i = 0; i < RetriesCount; i++)
            {
                attachment = store2.DatabaseCommands.GetAttachment("ayende");
                if(attachment == null)
                    Thread.Sleep(100);
            }
            Assert.NotNull(attachment);
            Assert.Equal(new byte[]{1,2,3}, attachment.Data);

        }

        [Fact]
        public void Can_replicate_large_number_of_documents_between_two_instances()
        {
            var store1 = CreateStore();
            var store2 = CreateStore();

            TellFirstInstanceToReplicateToSecondInstance();

            var databaseCommands = store1.DatabaseCommands;
            for (int i = 0; i < 150; i++)
            {
                databaseCommands.PutAttachment(i.ToString(), null, new byte[] {(byte) i}, new JObject());
            }

            bool foundAll = false;
            for (int i = 0; i < RetriesCount; i++)
            {
                var countFound = 0;
                for (int j = 0; j < 150; j++)
                {
                    var attachment = store2.DatabaseCommands.GetAttachment(i.ToString());
                    if (attachment == null)
                        break;
                    countFound++;
                }
                foundAll = countFound == 150;
                if (foundAll)
                    break;
                Thread.Sleep(100);
            }
            Assert.True(foundAll);
        }

        [Fact]
        public void Can_replicate_delete_between_two_instances()
        {
            var store1 = CreateStore();
            var store2 = CreateStore();

            TellFirstInstanceToReplicateToSecondInstance();

            store1.DatabaseCommands.PutAttachment("ayende", null, new byte[] { 2 }, new JObject());
          

            for (int i = 0; i < RetriesCount; i++)
            {
                if (store2.DatabaseCommands.GetAttachment("ayende") != null)
                    break;
                Thread.Sleep(100);
            }
            Assert.NotNull(store2.DatabaseCommands.GetAttachment("ayende"));

            store1.DatabaseCommands.DeleteAttachment("ayende", null);
         

            for (int i = 0; i < RetriesCount; i++)
            {
                if (store2.DatabaseCommands.GetAttachment("ayende") == null)
                    break;
                Thread.Sleep(100);
            }
            Assert.Null(store2.DatabaseCommands.GetAttachment("ayende"));
        }

        [Fact]
        public void When_replicating_and_an_attachment_is_already_there_will_result_in_conflict()
        {
            var store1 = CreateStore();
            var store2 = CreateStore();

            store1.DatabaseCommands.PutAttachment("ayende", null, new byte[] { 2 }, new JObject());


            store2.DatabaseCommands.PutAttachment("ayende", null, new byte[] { 3 }, new JObject());
          

            TellFirstInstanceToReplicateToSecondInstance();

            var conflictException = Assert.Throws<ConflictException>(() =>
            {
                for (int i = 0; i < RetriesCount; i++)
                {
                    store2.DatabaseCommands.GetAttachment("ayende");
          
                }
            });

            Assert.Equal("Conflict detected on ayende, conflict must be resolved before the attachment will be accessible", conflictException.Message);
        }


        [Fact]
        public void When_replicating_and_an_attachment_is_already_there_will_result_in_conflict_and_can_get_all_conflicts()
        {
            var store1 = CreateStore();
            var store2 = CreateStore();

            store1.DatabaseCommands.PutAttachment("ayende", null, new byte[] { 2 }, new JObject());


            store2.DatabaseCommands.PutAttachment("ayende", null, new byte[] { 3 }, new JObject());


            TellFirstInstanceToReplicateToSecondInstance();

            var conflictException = Assert.Throws<ConflictException>(() =>
            {
                for (int i = 0; i < RetriesCount; i++)
                {
                    store2.DatabaseCommands.GetAttachment("ayende");

                }
            });

            Assert.True(conflictException.ConflictedVersionIds[0].StartsWith("ayende/conflicts/00000000-0000-"));
            Assert.True(conflictException.ConflictedVersionIds[1].StartsWith("ayende/conflicts/00000000-0000-"));
        }
    }
}