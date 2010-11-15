using System;
using Xunit;

namespace Raven.Tests.Bugs
{
	public class SerializingDates : LocalClientTest
	{
		[Fact]
		public void CanSaveAndReadDates()
		{
			using(var store = NewDocumentStore())
			{
				using(var session = store.OpenSession())
				{
					session.Store(new DocItem
					{
						CreationDate = new DateTime(2010,8,3)
					});
					session.SaveChanges();
				}

				using(var session = store.OpenSession())
				{
					var docItem = session.Load<DocItem>("docitems/1");
					Assert.Equal(new DateTime(2010, 8, 3), docItem.CreationDate);
				}
			}	
		}

		public class DocItem
		{
			public string Id { get; set; }
			public DateTime CreationDate { get; set; }
			public DocItem()
			{
				CreationDate = DateTime.Now;
			}
		}
	}
}
