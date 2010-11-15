using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Raven.Client.Client;
using Raven.Client.Indexes;
using Raven.Database.Extensions;
using Xunit;
using Raven.Database.Data;
using Raven.Client;
using System.IO;
using Raven.Client.Document;
using Raven.Client.Linq;
using System.Threading;
using System.Diagnostics;

/*
 * Different test using where clause
 */
namespace Raven.Tests.Linq
{
    public class UsingWhereConditions
    {
        [Fact]
        public void Can_Use_Where()
        {

            //When running in the XUnit GUI strange things happen is we just create a path relative to 
            //the .exe itself, so make our folder in the System temp folder instead ("<user>\AppData\Local\Temp")
            string directoryName =  Path.Combine(Path.GetTempPath(), "ravendb.RavenWhereTests");
            IOExtensions.DeleteDirectory(directoryName);

            using (var db = new EmbeddableDocumentStore() { DataDirectory = directoryName })
            {
                db.Initialize();

                string indexName = "CommitByRevision";
                using (var session = db.OpenSession())
	            {
                    AddData(session);                    

                    db.DatabaseCommands.DeleteIndex(indexName);
                    var result = db.DatabaseCommands.PutIndex<CommitInfo, CommitInfo>(indexName,
                            new IndexDefinition<CommitInfo, CommitInfo>()
                            {
                                Map = docs => from doc in docs
                                              select new { doc.Revision},
                            }, true);                    

                    WaitForQueryToComplete(session, indexName);

                    var Results = session.Query<CommitInfo>(indexName)
                                            .Where(x => x.Revision == 1);
					//There is one CommitInfo with Revision == 1
                    Assert.Equal(1, Results.ToArray().Count());

                    Results = session.Query<CommitInfo>(indexName)
                                            .Where(x => x.Revision == 0);
                    //There is not CommitInfo with Revision = 0 so hopefully we do not get any result
                    Assert.Equal(0, Results.ToArray().Count());

					Results = session.Query<CommitInfo>(indexName)
											.Where(x => x.Revision < 1 );
					//There are 0 CommitInfos which has Revision <1 
					Assert.Equal(0, Results.ToArray().Count());

					Results = session.Query<CommitInfo>(indexName)
                                            .Where(x => x.Revision <  2);
                    //There is one CommitInfo with Revision < 2
                    Assert.Equal(1, Results.ToArray().Count());
					//Revision of resulted CommitInfo has to be 1
					var cinfo = Results.ToArray()[0];
					Assert.Equal(1, cinfo.Revision);

					Results = session.Query<CommitInfo>(indexName)
                                            .Where(x => x.Revision <= 2);
					//There are 2 CommitInfos which has Revision <=2 
					Assert.Equal(2, Results.ToArray().Count());


					Results = session.Query<CommitInfo>(indexName)
                                            .Where(x => x.Revision > 7);
					//There are 0 CommitInfos which has Revision >7 
					Assert.Equal(0, Results.ToArray().Count());

					Results = session.Query<CommitInfo>(indexName)
                                            .Where(x => x.Revision > 6);
					//There are 1 CommitInfos which has Revision >6 
					Assert.Equal(1, Results.ToArray().Count());

					Results = session.Query<CommitInfo>(indexName)
                                            .Where(x => x.Revision >= 6);
					//There are 2 CommitInfos which has Revision >=6 
					Assert.Equal(2, Results.ToArray().Count());

					Results = session.Query<CommitInfo>(indexName)
                                            .Where(x => x.Revision > 6 && x.Revision < 6);
					//There are 0 CommitInfos which has Revision >6 && <6 
					Assert.Equal(0, Results.ToArray().Count());

					Results = session.Query<CommitInfo>(indexName)
                                            .Where(x => x.Revision >= 6 && x.Revision <= 6);
					//There are 1 CommitInfos which has Revision >=6 && <=6 
					Assert.Equal(1, Results.ToArray().Count());

					Results = session.Query<CommitInfo>(indexName)
											.Where(x => x.Revision >= 6 && x.Revision < 6);
					//There are 0 CommitInfos which has Revision >=6 && <6 
					Assert.Equal(0, Results.ToArray().Count());

					Results = session.Query<CommitInfo>(indexName)
											.Where(x => x.Revision > 6 && x.Revision <= 6);
					//There are 0 CommitInfos which has Revision >6 && <=6 
					Assert.Equal(0, Results.ToArray().Count());

					Results = session.Query<CommitInfo>(indexName)
											.Where(x => x.Revision >= 7 && x.Revision <= 1);
					//There are 0 CommitInfos which has Revision >=7  && <= 1 
					Assert.Equal(0, Results.ToArray().Count());

					Results = session.Query<CommitInfo>(indexName)
											.Where(x => x.Revision > 7 && x.Revision < 1);
					//There are 0 CommitInfos which has Revision >7  && < 1 
					Assert.Equal(0, Results.ToArray().Count());


					Results = session.Query<CommitInfo>(indexName)
                                            .Where(x => x.Revision > 7 || x.Revision < 1);
					//There are 0 CommitInfos which has Revision >7  || < 1 
					Assert.Equal(0, Results.ToArray().Count());

					Results = session.Query<CommitInfo>(indexName)
											.Where(x => x.Revision >= 7 || x.Revision < 1);
					//There are 1 CommitInfos which has Revision >=7  || < 1 
					Assert.Equal(1, Results.ToArray().Count());

					Results = session.Query<CommitInfo>(indexName)
											.Where(x => x.Revision > 7 || x.Revision <= 1);
					//There are 1 CommitInfos which has Revision >7  || <= 1 
					Assert.Equal(1, Results.ToArray().Count());

					Results = session.Query<CommitInfo>(indexName)
                                            .Where(x => x.Revision >= 7 || x.Revision <= 1);
					//There are 2 CommitInfos which has Revision >=7  || <= 1 
					Assert.Equal(2, Results.ToArray().Count());
	            }
            }            
        }

		private static string Repo = "/svn/repo/";
        private static void WaitForQueryToComplete(IDocumentSession session, string indexName)
        {            
            QueryResult results;
            do
            {
                //doesn't matter what the query is here, just want to see if it's stale or not
                results = session.Advanced.LuceneQuery<CommitInfo>(indexName)                              
                              .Where("") 
                              .QueryResult;   

                if (results.IsStale)
                    Thread.Sleep(1000);
            } while (results.IsStale);            
        }

        private void AddData(IDocumentSession documentSession)
        {
            documentSession.Store(new CommitInfo { Author="kenny", PathInRepo="/src/test/", Repository=Repo, Revision=1, Date= DateTime.Now, CommitMessage="First commit" });
			documentSession.Store(new CommitInfo { Author = "kenny", PathInRepo = "/src/test/FirstTest/", Repository = Repo, Revision = 2, Date = DateTime.Now, CommitMessage = "Second commit" });
			documentSession.Store(new CommitInfo { Author = "kenny", PathInRepo = "/src/test/FirstTest/test.txt", Repository = Repo, Revision = 3, Date = DateTime.Now, CommitMessage = "Third commit" });
			documentSession.Store(new CommitInfo { Author = "john", PathInRepo = "/src/test/SecondTest/", Repository = Repo, Revision = 4, Date = DateTime.Now, CommitMessage = "Fourth commit" });
			documentSession.Store(new CommitInfo { Author = "john", PathInRepo = "/src/", Repository = Repo, Revision = 5, Date = DateTime.Now, CommitMessage = "Fifth commit" });
			documentSession.Store(new CommitInfo { Author = "john", PathInRepo = "/src/test/SecondTest/test.txt", Repository = Repo, Revision = 6, Date = DateTime.Now, CommitMessage = "Sixt commit" });
			documentSession.Store(new CommitInfo { Author = "kenny", PathInRepo = "/src/test/SecondTest/test1.txt", Repository = Repo, Revision = 7, Date = DateTime.Now, CommitMessage = "Seventh commit" });
            documentSession.SaveChanges();
        }
    }
}
