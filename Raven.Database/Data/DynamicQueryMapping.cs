using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Raven.Database.Indexing;
using System.Text.RegularExpressions;
using Raven.Http;

namespace Raven.Database.Data
{
    public class DynamicQueryMapping
    {
        public string ForEntityName { get; set; }

        public DynamicQueryMapping()
        {
            Items = new DynamicQueryMappingItem[0];
            SortDescriptors = new DynamicSortInfo[0];
        }

        public DynamicSortInfo[] SortDescriptors
        {
            get;
            set;
        }

        public DynamicQueryMappingItem[] Items
        {
            get;
            set;
        }

        public IndexDefinition CreateIndexDefinition()
        {
            var fromClauses = new HashSet<string>();
            var realMappings = new List<string>();
            
            if (!string.IsNullOrEmpty(this.ForEntityName))
            {
                fromClauses.Add("from doc in docs." + this.ForEntityName);
            }
            else
            {
                fromClauses.Add("from doc in docs");
            }

            foreach (var map in Items)
            {
                String currentDoc = "doc";
                StringBuilder currentExpression = new StringBuilder();

                int currentIndex = 0;
                while (currentIndex < map.From.Length)
                {
                    char currentChar = map.From[currentIndex++];
                    switch (currentChar)
                    {
                        case ',':                                                       

                            // doc.NewDoc.Items
                            String newDocumentSource =  string.Format("{0}.{1}", currentDoc, currentExpression.ToString());

                            // docNewDocItemsItem
                            String newDoc = string.Format("{0}Item", newDocumentSource.Replace(".", ""));

                            // from docNewDocItemsItem in doc.NewDoc.Items
                            String docInclude = string.Format("from {0} in {1}", newDoc, newDocumentSource);
                            fromClauses.Add(docInclude);

                            // Start building the property again
                            currentExpression.Clear();

                            // And from this new doc
                            currentDoc = newDoc;

                            break;
                        default:
                            currentExpression.Append(currentChar);
                            break;
                    }
                }

                // We get rid of any _Range(s) etc
                var indexedMember = currentExpression.ToString().Replace("_Range", "");
                if(indexedMember.Length == 0)
                {
                    realMappings.Add(string.Format("{0} = {1}",
                        map.To.Replace("_Range", ""),
                        currentDoc
                        ));
                }
                else
                {
                    realMappings.Add(string.Format("{0} = {1}.{2}",
                        map.To.Replace("_Range", ""),
                        currentDoc,
                        indexedMember
                        ));
                }
            }

           var index =  new IndexDefinition()
            {
                Map = string.Format("{0}\r\nselect new {{ {1} }}",
                   string.Join("\r\n", fromClauses.ToArray()),
                   string.Join(", ", realMappings.ToArray()))
            };

           foreach (var descriptor in this.SortDescriptors)
           {
               index.SortOptions.Add(descriptor.Field, (SortOptions)Enum.Parse(typeof(SortOptions), descriptor.FieldType)); 
           }      
           return index;
        }

        public static DynamicQueryMapping Create(string query, string entityName)
        {
            var fields = SimpleQueryParser.GetFields(query);

            var headers = CurrentOperationContext.Headers.Value;

            var sortInfo = new List<DynamicSortInfo>();
            String[] sortHintHeaders = headers.AllKeys
               .Where(key => key.StartsWith("SortHint")).ToArray();
            foreach (string sortHintHeader in sortHintHeaders)
            {
                String[] split = sortHintHeader.Split('_');
                String fieldName = split[1];
                string fieldType = headers[sortHintHeader];

                sortInfo.Add(new DynamicSortInfo()
                {
                    Field = fieldName,
                    FieldType = fieldType
                });

                fields.Add(fieldName);
            }
            
            return new DynamicQueryMapping()
            {
                ForEntityName = entityName,
                SortDescriptors = sortInfo.ToArray(),
                Items = fields.Select(x => new DynamicQueryMappingItem()
                {
                    From = x,
                    To = x.Replace(".", "").Replace(",", "")
                }).ToArray()
            };                      
        }

        

        public class DynamicSortInfo
        {
            public string Field { get; set; }
            public string FieldType { get; set; }
        }
    }
}
