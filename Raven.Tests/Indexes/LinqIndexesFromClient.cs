using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using Newtonsoft.Json.Linq;
using Raven.Client.Document;
using Raven.Client.Indexes;
using Raven.Database.Indexing;
using Raven.Database.Json;
using Raven.Database.Linq;
using Raven.Database.Plugins;
using Xunit;

namespace Raven.Tests.Indexes
{
	public class LinqIndexesFromClient
	{
        [Fact]
        public void Convert_select_many_will_keep_doc_id()
        {
            IndexDefinition indexDefinition = new IndexDefinition<Order>
            {
                Map = orders => from order in orders
                                from line in order.OrderLines
                                select new { line.ProductId }
            }.ToIndexDefinition(new DocumentConvention());
			var generator = new DynamicViewCompiler("test", indexDefinition, new AbstractDynamicCompilationExtension[0])
                .GenerateInstance();


            var results = generator.MapDefinition(new[]
			{
				GetDocumentFromString(
				@"
                {
                    '@metadata': {'Raven-Entity-Name': 'Orders', '@id': 1},
                    'OrderLines': [{'ProductId': 2}, {'ProductId': 3}]
                }"),
				  GetDocumentFromString(
				@"
                {
                    '@metadata': {'Raven-Entity-Name': 'Orders', '@id': 2},
                    'OrderLines': [{'ProductId': 5}, {'ProductId': 4}]
                }")
			}).Cast<object>().ToArray();

            foreach (var result in results)
            {
                Assert.NotNull(TypeDescriptor.GetProperties(result).Find("__document_id", true));
            }
        }

        public static dynamic GetDocumentFromString(string json)
        {
            return JsonToExpando.Convert(JObject.Parse(json));
        }

		[Fact]
		public void CanCompileComplexQuery()
		{
			var indexDefinition = new IndexDefinition<Person>()
			{
				Map = people => from person in people
				                from role in person.Roles
				                where role == "Student"
				                select new { role }
			}.ToIndexDefinition(new DocumentConvention());

			new DynamicViewCompiler("test", indexDefinition, new AbstractDynamicCompilationExtension[0])
                .GenerateInstance();
		}

		public class Person
		{
			public string[] Roles { get; set; }
		}


	    [Fact]
		public void Convert_simple_query()
		{
			IndexDefinition generated = new IndexDefinition<User, Named>
			{
				Map = users => from user in users
							   where user.Location == "Tel Aviv"
							   select new { user.Name },
				Stores = { { user => user.Name, FieldStorage.Yes } }
			}.ToIndexDefinition(new DocumentConvention());
			var original = new IndexDefinition
			{
				Stores = { { "Name", FieldStorage.Yes } },
				Map = @"docs.Users
	.Where(user => user.Location == ""Tel Aviv"")
	.Select(user => new {Name = user.Name})"
			};

			Assert.Equal(original, generated);
		}

		[Fact]
		public void Convert_using_id()
		{
			IndexDefinition generated = new IndexDefinition<User, Named>
			{
				Map = users => from user in users
							   where user.Location == "Tel Aviv"
							   select new { user.Name, user.Id },
				Stores = { { user => user.Name, FieldStorage.Yes } }
			}.ToIndexDefinition(new DocumentConvention());
			var original = new IndexDefinition
			{
				Stores = { { "Name", FieldStorage.Yes } },
				Map = @"docs.Users
	.Where(user => user.Location == ""Tel Aviv"")
	.Select(user => new {Name = user.Name, Id = user.__document_id})"
			};

			Assert.Equal(original, generated);
		}

		[Fact]
		public void Convert_simple_query_with_not_operator_and_nested_braces()
		{
			IndexDefinition generated = new IndexDefinition<User, Named>
			{
				Map = users => from user in users
				               where !(user.Location == "Te(l) (A)viv")
				               select new {user.Name},
				Stores = {{user => user.Name, FieldStorage.Yes}}
			}.ToIndexDefinition(new DocumentConvention());
			var original = new IndexDefinition
			{
				Stores = {{"Name", FieldStorage.Yes}},
				Map =
					@"docs.Users
	.Where(user => !(user.Location == ""Te(l) (A)viv""))
	.Select(user => new {Name = user.Name})"
			};

			Assert.Equal(original, generated);
		}

        [Fact]
        public void Convert_map_reduce_query()
        {
            IndexDefinition generated = new IndexDefinition<User, LocationCount>
            {
                Map = users => from user in users
                               select new { user.Location, Count = 1 },
                Reduce = counts => from agg in counts
                                   group agg by agg.Location
                                       into g
                                       select new { Location = g.Key, Count = g.Sum(x => x.Count) },
            }.ToIndexDefinition(new DocumentConvention());
            var original = new IndexDefinition
            {
                Map = @"docs.Users
	.Select(user => new {Location = user.Location, Count = 1})",
                Reduce = @"results
	.GroupBy(agg => agg.Location)
	.Select(g => new {Location = g.Key, Count = g.Sum(x => x.Count)})"
            };

            Assert.Equal(original, generated);
        }


#if !NET_3_5        
        public void Convert_map_reduce_query_with_map_(Expression<Func<IEnumerable<User>, IEnumerable>> mapExpression, string expectedIndexString)
        {
            IndexDefinition generated = new IndexDefinition<User, LocationCount>
            {
                Map = mapExpression,
                Reduce = counts => from agg in counts
                                   group agg by agg.Location
                                       into g
                                       select new { Location = g.Key, Count = g.Sum(x => x.Count) },
            }.ToIndexDefinition(new DocumentConvention());
            var original = new IndexDefinition
            {
                Map = expectedIndexString,
                Reduce = @"results
	.GroupBy(agg => agg.Location)
	.Select(g => new {Location = g.Key, Count = g.Sum(x => x.Count)})"
            };

            Assert.Equal(original, generated);
        }

        [Fact]
        public void Convert_map_reduce_preserving_parenthesis()
        {
            Convert_map_reduce_query_with_map_(
users => from user in users
         select new { Location = user.Location, Count = (user.Age + 3) * (user.Age + 4) },
@"docs.Users
	.Select(user => new {Location = user.Location, Count = (user.Age + 3) * (user.Age + 4)})");
        }

        [Fact]
        public void Convert_map_reduce_query_with_trinary_conditional()
        {
            Convert_map_reduce_query_with_map_(
users => from user in users
         select new { Location = user.Location, Count = user.Age >= 1 ? 1 : 0 }, 
@"docs.Users
	.Select(user => new {Location = user.Location, Count = user.Age >= 1 ? 1 : 0})");
        }

        [Fact]
        public void Convert_map_reduce_query_with_enum_comparison()
        {
            Convert_map_reduce_query_with_map_(
users => from user in users
        select new { Location = user.Location, Count = user.Gender == Gender.Female ? 1 : 0},
@"docs.Users
	.Select(user => new {Location = user.Location, Count = user.Gender == ""Female"" ? 1 : 0})");
        }

        [Fact]
        public void Convert_map_reduce_query_with_type_check()
        {
            Convert_map_reduce_query_with_map_(
users => from user in users
         select new { Location = user.Location, Count = user.Location is String ? 1 : 0 },
@"docs.Users
	.Select(user => new {Location = user.Location, Count = user.Location is String ? 1 : 0})");
        }



#endif

        public enum Gender
        {
            Male,
            Female
        }

        public class User
		{
			public string Id { get; set; }
			public string Name { get; set; }
			public string Location { get; set; }

			public int Age { get; set; }
            public Gender Gender { get; set; }
		}

		public class Named
		{
			public string Name { get; set; }
		}
		public class LocationCount
		{
			public string Location { get; set; }
			public int Count { get; set; }
		}


		public class LocationAge
		{
			public string Location { get; set; }
			public decimal Age { get; set; }
		}

        public class Order
        {
            public string Id { get; set; }
            public string Customer { get; set; }
            public IList<OrderLine> OrderLines { get; set; }

            public Order()
            {
                OrderLines = new List<OrderLine>();
            }
        }

        public class OrderLine
        {
            public string ProductId { get; set; }
            public int Quantity { get; set; }
        }
	}
}
