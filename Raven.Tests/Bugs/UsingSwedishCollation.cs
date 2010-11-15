using System;
using System.Globalization;
using Lucene.Net.Util;
using Raven.Database.Indexing;
using Raven.Database.Indexing.Collation;
using Raven.Database.Indexing.Collation.Cultures;
using Raven.Tests.Linq;
using Xunit;
using System.Linq;

namespace Raven.Tests.Bugs
{
    public class UsingSwedishCollation : LocalClientTest
    {
        [Fact]
        public void Can_sort_according_to_swedish_sorting_rules()
        {
            var names = new[]{"a", "b", "�", "�", "�"};
            var sortedNames = new[] {"a", "b", "�", "�", "�"};

            using (var store = NewDocumentStore())
            {
                store.DatabaseCommands.PutIndex("test",
                                                new IndexDefinition
                                                {
                                                    Map = "from doc in docs select new { doc.Name }",
                                                    SortOptions = {{"Name", SortOptions.String}},
                                                    Analyzers = {{"Name", typeof(SvCollationAnalyzer).AssemblyQualifiedName}}
                                                });

                using(var s = store.OpenSession())
                {
                    for (int i = 0; i < names.Length; i++)
                    {
                        s.Store(new Linq.User()
                        {
                            Age = i,
                            Info = names[i],
                            Name = names[i]
                        });
                    }

                    s.SaveChanges();
                }

                using (var s = store.OpenSession())
                {
                    var users = s.Query<Linq.User>("test")
                        .Customize(x=>x.WaitForNonStaleResults(TimeSpan.FromHours(1)))
                        .OrderBy(x=>x.Name).ToList();

                    for (int i = 0; i < names.Length; i++)
                    {
                        Assert.Equal(sortedNames[i], users[i].Info);
                    }
                }
            }
        }
    }
}