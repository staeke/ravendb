using System;
using System.Text;
using System.Text.RegularExpressions;

using Lucene.Net.Analysis;
using Lucene.Net.Search;
using Version = Lucene.Net.Util.Version;

namespace Raven.Database.Indexing
{
	
	public static class QueryBuilder
	{
		static readonly Regex untokenizedQuery = new Regex(@"([\w\d_]+?):(\[\[.+?\]\])", RegexOptions.Compiled);

		public static Query BuildQuery(string query, PerFieldAnalyzerWrapper analyzer)
		{
			var keywordAnalyzer = new KeywordAnalyzer();
			try
		    {
		    	query = PreProcessUntokenizedTerms(analyzer, query, keywordAnalyzer);
		    	var queryParser = new RangeQueryParser(Version.LUCENE_29, "", analyzer);
				queryParser.SetAllowLeadingWildcard(true);
		    	return queryParser.Parse(query);;
			}
		    finally
		    {
				keywordAnalyzer.Close();
		    }
		}

		/// <summary>
		/// Detects untokenized fields and sets as NotAnalyzed in analyzer
		/// </summary>
		private static string PreProcessUntokenizedTerms(PerFieldAnalyzerWrapper analyzer, string query, Analyzer keywordAnlyzer)
		{
			var untokenizedMatches = untokenizedQuery.Matches(query);
			if (untokenizedMatches.Count < 1)
			{
				return query;
			}

			var sb = new StringBuilder(query);

			// KeywordAnalyzer will not tokenize the values

			// process in reverse order to leverage match string indexes
			for (int i = untokenizedMatches.Count; i > 0; i--)
			{
				Match match = untokenizedMatches[i - 1];

				// specify that term for this field should not be tokenized
				analyzer.AddAnalyzer(match.Groups[1].Value, keywordAnlyzer);

				Group term = match.Groups[2];

				// introduce " " around the term
				var startIndex = term.Index;
				var length = term.Length-2;
				if (sb[startIndex + length - 1] != '"')
				{
					sb.Insert(startIndex + length, '"');
					length += 1;
				}
				if (sb[startIndex+2] != '"')
				{
					sb.Insert(startIndex+2, '"');
					length += 1;
				}
				// remove enclosing "[[" "]]" from term value (again in reverse order)
				sb.Remove(startIndex + length, 2);
				sb.Remove(startIndex, 2);
				
			}

			return sb.ToString();
		}
	}
}
