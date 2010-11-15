using System;
using System.ComponentModel.Composition.Hosting;
using Newtonsoft.Json.Linq;
using Raven.Database;
using Raven.Database.Config;
using Raven.Database.Exceptions;
using Raven.Tests.Storage;
using Xunit;

namespace Raven.Tests.Triggers
{
    public class AttachmentPutTriggers: AbstractDocumentStorageTest
	{
		private readonly DocumentDatabase db;

		public AttachmentPutTriggers()
		{
			db = new DocumentDatabase(new RavenConfiguration
			{
				DataDirectory = "raven.db.test.esent",
				Container = new CompositionContainer(new TypeCatalog(
					typeof(AuditAttachmentPutTrigger),
                    typeof(RefuseBigAttachmentPutTrigger))),
				RunInUnreliableYetFastModeThatIsNotSuitableForProduction = true
			});
		}

		public override void Dispose()
		{
			db.Dispose();
			base.Dispose();
		}


        [Fact]
        public void CanModifyAttachmentPut()
        {
            db.PutStatic("ayende", null, new byte[]{1,2,3}, new JObject());

            Assert.Equal(new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc), db.GetStatic("ayende").Metadata.Value<DateTime>("created_at"));
        }


        [Fact]
        public void CanVetoAttachmentPut()
        {
            var operationVetoedException = Assert.Throws<OperationVetoedException>(() =>
                                                                                       db.PutStatic("ayende", null, new byte[] {1, 2, 3, 4, 5, 6},
                                                                                                    new JObject()));

            Assert.Equal("PUT vetoed by Raven.Tests.Triggers.RefuseBigAttachmentPutTrigger because: Attachment is too big", operationVetoedException.Message);
        }
	}
}