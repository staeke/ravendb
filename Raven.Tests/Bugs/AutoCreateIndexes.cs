using System.Linq;
using Raven.Client.Indexes;
using Raven.Database.Indexing;
using Xunit;

namespace Raven.Tests.Bugs
{
	public class AutoCreateIndexes : LocalClientTest
	{
		[Fact]
		public void CanAutomaticallyCreateIndexes()
		{
			using (var store = NewDocumentStore())
			{
				IndexCreation.CreateIndexes(typeof(Movies_ByActor).Assembly, store);

				using (var s = store.OpenSession())
				{
					s.Store(new Movie
					{
						Name = "Hello Dolly",
						Tagline = "She's a jolly good"
					});
					s.SaveChanges();
				}

				using (var s = store.OpenSession())
				{
                    var movies = s.Advanced.LuceneQuery<Movie>("Movies/ByActor")
						.Where("Name:Dolly")
						.WaitForNonStaleResults()
						.ToList();

					Assert.Equal(1, movies.Count);
				}
			}
		}

		public class Movies_ByActor : AbstractIndexCreationTask<Movie>
		{
		    public Movies_ByActor()
		    {
		        Map = movies => from movie in movies
		                        select new {movie.Name};
                Index(x=>x.Name, FieldIndexing.Analyzed);
		    }
		}

		public class Movie
		{
			public string Id { get; set; }
			public string Name { get; set; }
			public string Tagline { get; set; }
		}
	}
}
