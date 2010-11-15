using Raven.Database.Indexing;
using Xunit;

namespace Raven.Tests.Bugs
{
    public class OverwriteIndexLocally : LocalClientTest
    {
        [Fact]
        public void CanOverwriteIndex()
        {
            using(var store = NewDocumentStore())
            {
                store.DatabaseCommands.PutIndex("test",
                                                new IndexDefinition
                                                {
                                                    Map = "from doc in docs select new { doc.Name }"
                                                }, overwrite:true);


                store.DatabaseCommands.PutIndex("test",
                                               new IndexDefinition
                                               {
                                                   Map = "from doc in docs select new { doc.Name }"
                                               }, overwrite: true);

                store.DatabaseCommands.PutIndex("test",
                                                new IndexDefinition
                                                {
                                                    Map = "from doc in docs select new { doc.Email }"
                                                }, overwrite: true);

                store.DatabaseCommands.PutIndex("test",
                                           new IndexDefinition
                                           {
                                               Map = "from doc in docs select new { doc.Email }"
                                           }, overwrite: true);
            }
        }
    }
}
