using System.Linq;
using Raven.Client.Document;
using Raven.Client.Indexes;
using Raven.Database.Indexing;
using Xunit;

namespace Raven.Tests.Bugs
{
    public class TranslatingLinqQueryUsingNestedId : LocalClientTest
    {
        [Fact]
        public void Id_on_member_should_not_be_converted_to_document_id()
        {
            var generated = new IndexDefinition<SubCategory>
            {
                Map = subs => from subCategory in subs
                              select new
                              {
                                  CategoryId = subCategory.Id,
                                  SubCategoryId = subCategory.Parent.Id
                              }
            }.ToIndexDefinition(new DocumentConvention());
            var original = new IndexDefinition
            {
                Map =
                    @"docs.SubCategories
	.Select(subCategory => new {CategoryId = subCategory.__document_id, SubCategoryId = subCategory.Parent.Id})"
            };

            Assert.Equal(original.Map, generated.Map);
        }

        #region Nested type: Category

        public class Category
        {
            public string Id { get; set; }
        }

        #endregion

        #region Nested type: SubCategory

        public class SubCategory
        {
            public string Id { get; set; }
            public Category Parent { get; set; }
        }

        #endregion
    }
}