using System;
using System.ComponentModel.Composition.Hosting;
using System.Linq;
using Newtonsoft.Json.Linq;
using Raven.Database;
using Raven.Database.Config;
using Raven.Database.Data;
using Raven.Database.Indexing;
using Raven.Database.Plugins;
using Raven.Http;
using Raven.Tests.Storage;
using Xunit;

namespace Raven.Tests.Triggers
{
	public class ReadTriggers : AbstractDocumentStorageTest
	{
		private readonly DocumentDatabase db;

		public ReadTriggers()
		{
			db = new DocumentDatabase(new RavenConfiguration
			{
				DataDirectory = "raven.db.test.esent",
				Container = new CompositionContainer(new TypeCatalog(
					typeof(VetoReadsOnCapitalNamesTrigger),
					typeof(HiddenDocumentsTrigger),
					typeof(UpperCaseNamesTrigger)))
			});
			db.SpinBackgroundWorkers();
			db.PutIndex("ByName",
			            new IndexDefinition
			            {
			            	Map = "from doc in docs select new{ doc.name}"
			            });
		}

		public override void Dispose()
		{
			db.Dispose();
			base.Dispose();
		}
		
		[Fact]
		public void CanFilterAccessToDocumentUsingTrigger_Get()
		{
			db.Put("abc", null, JObject.Parse("{'name': 'abC'}"), new JObject(), null);

			var jsonDocument = db.Get("abc", null);

			Assert.Equal("Upper case characters in the 'name' property means the document is a secret!",
				jsonDocument.Metadata.Value<JObject>("Raven-Read-Veto").Value<string>("Reason"));
		}

		[Fact]
		public void CanFilterAccessToDocumentUsingTrigger_GetDocuments()
		{
			db.Put("abc", null, JObject.Parse("{'name': 'abC'}"), new JObject(), null);

			var jsonDocument = db.GetDocuments(0,25,null).First();

			Assert.Equal("Upper case characters in the 'name' property means the document is a secret!",
				jsonDocument.Value<JObject>("@metadata").Value<JObject>("Raven-Read-Veto").Value<string>("Reason"));
		}

		[Fact]
		public void CanFilterAccessToDocumentUsingTrigger_Query()
		{
			db.Put("abc", null, JObject.Parse("{'name': 'abC'}"), new JObject(), null);

			QueryResult queryResult;
			do
			{
				queryResult = db.Query("ByName", new IndexQuery
				{
					Query = "name:abC",
					PageSize = 10
				});
			} while (queryResult.IsStale);

			Assert.Equal("Upper case characters in the 'name' property means the document is a secret!",
				queryResult.Results[0].Value<JObject>("@metadata").Value<JObject>("Raven-Read-Veto").Value<string>("Reason"));
		}

		[Fact]
		public void CanCompleteHideDocumentUsingTrigger()
		{
			db.Put("abc", null, JObject.Parse("{'name': 'abc', 'hidden': true}"), new JObject(), null);

			var jsonDocument = db.Get("abc", null);

			Assert.Null(jsonDocument);
		}

		[Fact]
		public void CanCompleteHideDocumentUsingTrigger_GetDocuments()
		{
			db.Put("abc", null, JObject.Parse("{'name': 'abc', 'hidden': true}"), new JObject(), null);


            Assert.Empty(db.GetDocuments(0, 25, null));
		}

		[Fact]
		public void CanCompleteHideDocumentUsingTrigger_Query()
		{
			db.Put("abc", null, JObject.Parse("{'name': 'abc', 'hidden': true}"), new JObject(), null);

			QueryResult queryResult;
			do
			{
				queryResult = db.Query("ByName", new IndexQuery
				{
					Query = "name:abC",
					PageSize = 10
				});
			} while (queryResult.IsStale);

			Assert.Empty(queryResult.Results);
		}


        [Fact]
        public void CanPageThroughFilteredQuery()
        {
            for (int i = 0; i < 15; i++)
            {
                db.Put(i.ToString(), null, new JObject(
                                               new JProperty("hidden", i%2 == 0)), new JObject(), null);
            }

            QueryResult queryResult;
            do
            {
                queryResult = db.Query("ByName", new IndexQuery
                {
                    PageSize = 10
                });
            } while (queryResult.IsStale);

            Assert.Equal(7,queryResult.Results.Count);
            Assert.Equal(15, queryResult.TotalResults);
        }

        [Fact]
        public void CanPageThroughFilteredQuery_Page1()
        {
            for (int i = 0; i < 15; i++)
            {
                db.Put(i.ToString(), null, new JObject(
                                               new JProperty("hidden", i % 2 == 0)), new JObject(), null);
            }

            QueryResult queryResult;
            do
            {
                queryResult = db.Query("ByName", new IndexQuery
                {
                    PageSize = 3
                });
            } while (queryResult.IsStale);

            Assert.Equal(3, queryResult.Results.Count);
            var array = queryResult.Results.Select(x => int.Parse(x["@metadata"].Value<string>("@id"))).OrderBy(x => x).ToArray();
            Assert.Equal(1, array[0]);
            Assert.Equal(3, array[1]);
            Assert.Equal(5, array[2]);
        }

        [Fact]
        public void CanPageThroughFilteredQuery_Page2()
        {
            for (int i = 0; i < 15; i++)
            {
                db.Put(i.ToString(), null, new JObject(
                                               new JProperty("hidden", i % 2 == 0)), new JObject(), null);
            }

            QueryResult queryResult;
            do
            {
                queryResult = db.Query("ByName", new IndexQuery
                {
                    PageSize = 3,
                });
            } while (queryResult.IsStale);

            queryResult = db.Query("ByName", new IndexQuery
            {
                PageSize = 3,
                Start = queryResult.SkippedResults + queryResult.Results.Count
            });

            Assert.Equal(3, queryResult.Results.Count);
            var array = queryResult.Results.Select(x => int.Parse(x["@metadata"].Value<string>("@id"))).OrderBy(x => x).ToArray();
            Assert.Equal(7, array[0]);
            Assert.Equal(9, array[1]);
            Assert.Equal(11, array[2]);
        }


		[Fact]
		public void CanModifyDocumentUsingTrigger()
		{
			db.Put("abc", null, JObject.Parse("{'name': 'abc'}"), new JObject(), null);

			var jsonDocument = db.Get("abc", null);

			Assert.Equal("ABC", jsonDocument.DataAsJson.Value<string>("name"));
		}

		[Fact]
		public void CanModifyDocumentUsingTrigger_GetDocuments()
		{
			db.Put("abc", null, JObject.Parse("{'name': 'abc'}"), new JObject(), null);


            Assert.Equal("ABC", db.GetDocuments(0, 10, null).First().Value<string>("name"));
		}

		[Fact]
		public void CanModifyDocumentUsingTrigger_Query()
		{
			db.Put("abc", null, JObject.Parse("{'name': 'abc'}"), new JObject(), null);

			QueryResult queryResult;
			do
			{
				queryResult = db.Query("ByName", new IndexQuery
				{
					Query = "name:abC",
					PageSize = 10
				});
			} while (queryResult.IsStale);

			Assert.Equal("ABC", queryResult.Results[0].Value<string>("name"));
		}

		public class HiddenDocumentsTrigger : AbstractReadTrigger
		{
			public override ReadVetoResult AllowRead(string key, JObject document, JObject metadata, ReadOperation operation, TransactionInformation transactionInformation)
			{
				if (operation == ReadOperation.Index)
					return ReadVetoResult.Allowed; 
				var name = document["hidden"];
				if (name != null && name.Value<bool>())
				{
					return ReadVetoResult.Ignore;
				}
				return ReadVetoResult.Allowed;
			}
		}

		public class VetoReadsOnCapitalNamesTrigger : AbstractReadTrigger
		{
            public override ReadVetoResult AllowRead(string key, JObject document, JObject metadata, ReadOperation operation, TransactionInformation transactionInformation)
			{
				if (operation == ReadOperation.Index)
					return ReadVetoResult.Allowed;
				var name = document["name"];
				if (name != null && name.Value<string>().Any(char.IsUpper))
				{
					return ReadVetoResult.Deny("Upper case characters in the 'name' property means the document is a secret!");
				}
				return ReadVetoResult.Allowed;
			}
		}

		public class UpperCaseNamesTrigger : AbstractReadTrigger
		{
            public override void OnRead(string key, JObject document, JObject metadata, ReadOperation operation, TransactionInformation transactionInformation)
			{
				var name = document.Property("name");
				if (name != null)
				{
					name.Value = new JValue(name.Value.Value<string>().ToUpper());
				}
			}
		}
	}
}
