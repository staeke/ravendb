using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Raven.Database.Data;

namespace Raven.Client.Linq
{
	/// <summary>
	/// An implementation of <see cref="IRavenQueryProvider"/>
	/// </summary>
	public class RavenQueryProvider<T> :  IRavenQueryProvider
    {
        private readonly string indexName;
	    private readonly RavenQueryStatistics ravenQueryStatistics;
	    private Action<IDocumentQueryCustomization> customizeQuery;
	    private Action<QueryResult> afterQueryExecuted;
		private readonly IDocumentSession session;

        /// <summary>
        /// Gets the actions for customizing the generated lucene query
        /// </summary>
        public Action<IDocumentQueryCustomization> CustomizedQuery
        {
            get { return customizeQuery; }
        }

		/// <summary>
		/// Gets the session.
		/// </summary>
		/// <value>The session.</value>
		public IDocumentSession Session
        {
            get { return session; }
        }

		/// <summary>
		/// Gets the name of the index.
		/// </summary>
		/// <value>The name of the index.</value>
        public string IndexName
        {
            get { return indexName; }
        }

	    /// <summary>
	    /// Change the result type for the query provider
	    /// </summary>
	    public IRavenQueryProvider For<S>()
	    {
            if (typeof(T) == typeof(S))
                return this;

	        var ravenQueryProvider = new RavenQueryProvider<S>(session, indexName, ravenQueryStatistics);
	        ravenQueryProvider.Customize(customizeQuery);
	        return ravenQueryProvider;
	    }

	    /// <summary>
	    /// Initializes a new instance of the <see cref="RavenQueryProvider&lt;T&gt;"/> class.
	    /// </summary>
	    /// <param name="session">The session.</param>
	    /// <param name="indexName">Name of the index.</param>
	    /// <param name="ravenQueryStatistics"></param>
	    public RavenQueryProvider(IDocumentSession session, string indexName, RavenQueryStatistics ravenQueryStatistics)
        {
            this.session = session;
            this.indexName = indexName;
	        this.ravenQueryStatistics = ravenQueryStatistics;
        }

		/// <summary>
		/// Executes the query represented by a specified expression tree.
		/// </summary>
		/// <param name="expression">An expression tree that represents a LINQ query.</param>
		/// <returns>
		/// The value that results from executing the specified query.
		/// </returns>
		public virtual object Execute(Expression expression)
		{
			return new RavenQueryProviderProcessor<T>(session, customizeQuery, afterQueryExecuted, indexName).Execute(expression);
		}

		IQueryable<S> IQueryProvider.CreateQuery<S>(Expression expression)
        {
            return new RavenQueryInspector<S>(this, expression, ravenQueryStatistics);
        }

        IQueryable IQueryProvider.CreateQuery(Expression expression)
        {
            Type elementType = TypeSystem.GetElementType(expression.Type);
            try
            {
                return
                    (IQueryable)
                    Activator.CreateInstance(typeof(RavenQueryInspector<>).MakeGenericType(elementType),
                                             new object[] { this, expression });
            }
            catch (TargetInvocationException tie)
            {
                throw tie.InnerException;
            }
        }

		/// <summary>
		/// Executes the specified expression.
		/// </summary>
		/// <typeparam name="S"></typeparam>
		/// <param name="expression">The expression.</param>
		/// <returns></returns>
        S IQueryProvider.Execute<S>(Expression expression)
        {
            return (S)Execute(expression);
        }

		/// <summary>
		/// Executes the query represented by a specified expression tree.
		/// </summary>
		/// <param name="expression">An expression tree that represents a LINQ query.</param>
		/// <returns>
		/// The value that results from executing the specified query.
		/// </returns>
        object IQueryProvider.Execute(Expression expression)
        {
            return Execute(expression);
        }

        /// <summary>
        /// Callback to get the results of the query
        /// </summary>
	    public void AfterQueryExecuted(Action<QueryResult> afterQueryExecutedCallback)
	    {
	        this.afterQueryExecuted=afterQueryExecutedCallback;
	    }

	    /// <summary>
		/// Customizes the query using the specified action
		/// </summary>
		/// <param name="action">The action.</param>
        public virtual void Customize(Action<IDocumentQueryCustomization> action)
        {
            if (action == null)
                return;
            customizeQuery += action;
        }


	}
}
