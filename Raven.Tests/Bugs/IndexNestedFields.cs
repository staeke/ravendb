﻿using System;
using System.Linq;
using Raven.Client;
using Raven.Client.Indexes;
using Raven.Database.Indexing;
using Xunit;

namespace Raven.Tests.Bugs
{
    public class IndexNestedFields : LocalClientTest
    {
        public class Outer
        {
            public Middle middle;
        }

        public class Middle
        {
            public Inner inner;
        }

        public class Inner
        {
            public int ID;
        }

        static int ExpectedId = 1234;

        [Fact]
        public void can_query_by_ID()
        {
            UsingPrepoulatedDatabase(delegate(IDocumentSession session3)
            {
                var results1 = session3.Advanced.LuceneQuery<Outer>("matryoshka").Where("ID:" + ExpectedId).ToArray();

                Assert.Equal(ExpectedId, results1.Single().middle.inner.ID);
            });
        }

        [Fact]
        public void can_query_by_ID_with_linq()
        {
            UsingPrepoulatedDatabase(delegate(IDocumentSession session3)
                {
                    var results1 = session3.Query<Outer>("matryoshka")
                        .Where(d => d.middle.inner.ID == ExpectedId)
                        .ToArray();

                Assert.Equal(ExpectedId, results1.Single().middle.inner.ID);
            });
        }

        void UsingPrepoulatedDatabase(Action<IDocumentSession> testOperation)
        {
            using (var store = base.NewDocumentStore())
            {
                store.DatabaseCommands.PutIndex("matryoshka", new IndexDefinition<Outer, Outer>()
                    {
                        Map = docs => from doc in docs
                                      select new { doc.middle.inner.ID},
                        Indexes = { {d => d.middle.inner.ID, FieldIndexing.NotAnalyzed  }}
                    });

                using(var session = store.OpenSession())
                {
                    session.Store(new Outer
                        {
                            middle = new Middle()
                                {
                                    inner = new Inner()
                                        {
                                            ID = ExpectedId
                                        }
                                }
                        });
                    
                    session.SaveChanges();
                }

                WaitForIndexing(store);

                using (var session = store.OpenSession())
                {
                    testOperation(session);
                }
            }
        }
    }
}
