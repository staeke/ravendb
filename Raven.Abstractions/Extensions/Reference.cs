namespace Raven.Database.Extensions
{
	/// <summary>
	/// A reference that can be used with lambda expression
	/// to pass a value out.
	/// </summary>
	public class Reference<T>
	{
		/// <summary>
		/// Gets or sets the value.
		/// </summary>
		/// <value>The value.</value>
		public T Value { get; set; }
	}
}
