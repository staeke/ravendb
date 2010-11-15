using System;
using Raven.Database.Data;
using Raven.Database.Indexing;
using Xunit;

namespace Raven.Tests.Bugs
{
    public class WillThrowIfQueryingForUnindexedField : LocalClientTest
    {
        [Fact]
        public void ThrowOnMapIndex()
        {
            using(var store = NewDocumentStore())
            {
                store.DatabaseCommands.PutIndex("test", new IndexDefinition
                {
                    Map = "from u in docs select new { u.Name }"
                });
                
                store.DatabaseCommands.Query("test", new IndexQuery
                {
                    Query = "Name:Oren"
                }, new string[0]);

                var argumentException = Assert.Throws<ArgumentException>(() => store.DatabaseCommands.Query("test", new IndexQuery
                {
                    Query = "User:Oren"
                }, new string[0]));

                Assert.Equal("The field 'User' is not indexed, cannot query on fields that are not indexed", argumentException.Message);
            }
        }

        [Fact]
        public void ThrowOnReduceIndex()
        {
            using (var store = NewDocumentStore())
            {
                store.DatabaseCommands.PutIndex("test", new IndexDefinition
                {
                    Map = "from u in docs select new { u.Name }",
                    Reduce = "from u in results group u by u.Name into g select new { User = g.Key }"
                });

                store.DatabaseCommands.Query("test", new IndexQuery
                {
                    Query = "User:Oren"
                }, new string[0]);

                var argumentException = Assert.Throws<ArgumentException>(() => store.DatabaseCommands.Query("test", new IndexQuery
                {
                    Query = "Name:Oren"
                }, new string[0]));

                Assert.Equal("The field 'Name' is not indexed, cannot query on fields that are not indexed", argumentException.Message);
            }
        }

        [Fact]
        public void ThrowOnSortIndex()
        {
            using (var store = NewDocumentStore())
            {
                store.DatabaseCommands.PutIndex("test", new IndexDefinition
                {
                    Map = "from u in docs select new { u.Name }",
                });

                var argumentException = Assert.Throws<ArgumentException>(() => store.DatabaseCommands.Query("test", new IndexQuery
                {
                    Query = "Name:Oren",
                    SortedFields = new[]{new SortedField("User"), }
                }, new string[0]));

                Assert.Equal("The field 'User' is not indexed, cannot sort on fields that are not indexed", argumentException.Message);
            }
        }
    }
}